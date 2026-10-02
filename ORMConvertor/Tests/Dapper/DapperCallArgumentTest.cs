using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Dapper;

/// <summary>
/// What a Dapper call states beside its SQL (decisions 048 and 109). The reading used to take
/// the sql and skip the rest of the arguments without a word. Now each is told by its name or
/// by its position in the overload Dapper 2.1.79 declares, and answered by what it states: the
/// parameter object, the transaction and the result type are the caller's; buffered,
/// commandTimeout and a multi-mapping are losses; and a command type that makes the text
/// something other than a query refuses the query by name.
/// </summary>
public class DapperCallArgumentTest
{
    private const string Unit = "queries";

    private const string RichSql = "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > 2000";
    private const string OrderedSql = "SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName ASC";

    private static ConversionResult Convert(string call) =>
        ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.Dapper,
            [
                .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
                new()
                {
                    ContentType = ConversionContentType.CSharp,
                    Name = Unit,
                    Content = $$"""
                        public object Load(IDbConnection connection, IDbTransaction transaction, CommandType type)
                        {
                            return {{call}};
                        }
                        """,
                },
            ]);

    private static List<ConversionRecord> Records(ConversionResult result, ConversionRecordKind kind) =>
        [.. result.Records.Where(r => r.Kind == kind && r.Unit == Unit)];

    private static List<string> Queries(ConversionResult result) =>
        [.. result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpQuery).Select(s => s.Content)];

    /// <summary>
    /// The options that say how Dapper runs the command are each a loss that names the
    /// argument as the overload calls it, passed by name or by position - and the position
    /// of commandTimeout is not the same in Query, which has buffered before it, and in the
    /// rest. An argument the overload has no place for is a loss too.
    /// </summary>
    [Theory]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", commandTimeout: 5)", "commandTimeout: 5")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", buffered: false)", "buffered: false")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", null, transaction, false)", "buffered: false")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", null, null, true, 30)", "commandTimeout: 30")]
    [InlineData($"connection.QueryAsync<Customer>(\"{RichSql}\", null, null, 30)", "commandTimeout: 30")]
    [InlineData($"connection.QueryFirst<Customer>(\"{RichSql}\", new {{ }}, transaction, 5)", "commandTimeout: 5")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", retries: 3)", "retries: 3, which the reading does not know")]
    public void AnOptionOfTheCallIsALossThatNamesIt(string call, string named)
    {
        var result = Convert(call);

        Assert.Single(Queries(result));
        Assert.Empty(Records(result, ConversionRecordKind.Failure));
        var loss = Assert.Single(Records(result, ConversionRecordKind.Loss));
        Assert.Contains($"The Dapper call passes {named}", loss.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The result type, the parameter object and the transaction are values of the caller,
    /// not facts of the query, and an option that spells out its default states nothing.
    /// </summary>
    [Theory]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", new {{ minimum = 2000 }}, transaction, true, null, CommandType.Text)")]
    [InlineData($"connection.Query<Customer>(sql: \"{RichSql}\", transaction: transaction, commandType: System.Data.CommandType.Text)")]
    [InlineData($"connection.Query(typeof(Customer), \"{RichSql}\", null, transaction)")]
    [InlineData($"connection.QuerySingleAsync<Customer>(\"{RichSql}\", null, transaction, null, null)")]
    public void TheCallersValuesSayNothing(string call)
    {
        var result = Convert(call);

        Assert.Contains("2000", Assert.Single(Queries(result)), StringComparison.Ordinal);
        Assert.Empty(Records(result, ConversionRecordKind.Loss));
        Assert.Empty(Records(result, ConversionRecordKind.Failure));
    }

    /// <summary>
    /// With StoredProcedure Dapper sends the text as the name of a procedure, and with
    /// TableDirect as the name of a table: neither is a query, and a SELECT sent so fails on
    /// the server. A command type computed at run time leaves what the text is unknown. The
    /// query is refused by name in all three cases, whatever the text holds - a bare name
    /// included, which used to be refused for another reason, as a call of a procedure.
    /// </summary>
    [Theory]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", commandType: CommandType.StoredProcedure)", "name of a stored procedure")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", null, null, true, null, CommandType.StoredProcedure)", "name of a stored procedure")]
    [InlineData("connection.Query<Customer>(\"dbo.FindCustomers\", commandType: System.Data.CommandType.StoredProcedure)", "name of a stored procedure")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", commandType: CommandType.TableDirect)", "name of a table")]
    [InlineData($"connection.QueryFirst<Customer>(\"{RichSql}\", null, null, null, type)", "computed at run time")]
    public void ACommandTypeThatMakesTheTextNoQueryRefusesIt(string call, string reason)
    {
        var result = Convert(call);

        Assert.Empty(Queries(result));
        var failure = Assert.Single(Records(result, ConversionRecordKind.Failure));
        Assert.StartsWith("The Dapper call passes commandType: ", failure.Reason, StringComparison.Ordinal);
        Assert.Contains(reason, failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A multi-mapping says how each row becomes several objects, which every target derives
    /// again: one loss naming its arguments, whether the overload is told by its type
    /// arguments, by the lambda or by the list of types.
    /// </summary>
    [Theory]
    [InlineData($"connection.Query<Customer, Customer, Customer>(\"{RichSql}\", (a, b) => a, splitOn: \"CustomerName\")", "(map, splitOn)")]
    [InlineData($"connection.QueryAsync(\"{RichSql}\", (Customer a, Customer b) => a, null, transaction, true, \"CustomerName\")", "(map, splitOn)")]
    [InlineData($"connection.Query<Customer>(\"{RichSql}\", new[] {{ typeof(Customer), typeof(Customer) }}, row => (Customer)row[0], splitOn: \"CustomerName\")", "(types, map, splitOn)")]
    public void AMultiMappingIsOneLoss(string call, string named)
    {
        var result = Convert(call);

        Assert.Single(Queries(result));
        var loss = Assert.Single(Records(result, ConversionRecordKind.Loss));
        Assert.Contains($"maps each row onto several objects {named}", loss.Reason, StringComparison.Ordinal);
        Assert.Equal(QueryFeature.Projection, loss.Feature);
    }

    /// <summary>What a QueryMultiple states beside its text, it states of every query the text holds.</summary>
    [Fact]
    public void AnOptionOfQueryMultipleIsALossOfEachOfItsQueries()
    {
        var result = Convert($"connection.QueryMultiple(\"{RichSql}; {OrderedSql}\", commandTimeout: 5)");

        Assert.Equal(2, Queries(result).Count);
        var losses = Records(result, ConversionRecordKind.Loss);
        Assert.Equal(new[] { "Query01", "Query02" }, losses.Select(l => l.Query).Order());
        Assert.All(losses, l => Assert.Contains("commandTimeout: 5", l.Reason, StringComparison.Ordinal));
    }
}
