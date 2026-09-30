using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using DapperWrappers;
using Model;
using Model.AbstractRepresentation;
using MyBatisWrappers;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A bare SQL unit is a script (decision 108). T-SQL says what a text of several statements
/// means - a batch whose statements run in order and whose every SELECT returns a result set
/// of its own - so every SELECT of the unit is a query of its own, with a builder of its own,
/// and a unit of several SELECTs numbers them by their position, since a SELECT names
/// nothing. The unit used to be refused whole for its second SELECT, although each of them
/// could be translated.
///
/// The other side of the line: a text handed to one construct of a host framework - the
/// literal of a Dapper call, a MyBatis &lt;select&gt;, an NHibernate &lt;sql-query&gt; - is
/// one command that maps one result, and a second SELECT in it is refused as before. And in
/// every one of the four routes a statement that does not read refuses the whole text,
/// because the queries of a script are independent only while nothing in it writes.
/// </summary>
public class SqlScriptTest
{
    private const string Rich = "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > 2000";
    private const string Ordered = "SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName ASC";

    /// <summary>A SELECT the reading refuses by itself: FOR JSON returns one document instead of rows (decision 070).</summary>
    private const string Document = "SELECT c.CustomerName FROM Sales.Customers AS c FOR JSON AUTO";

    private static ConversionResult Convert(ORMEnum target, string script) =>
        ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
                new() { ContentType = ConversionContentType.SqlQuery, Content = script, Name = "reports.sql" },
            ]);

    private static List<string> Methods(ConversionResult result, ConversionContentType artifact) =>
        [.. result.Sources.Where(s => s.ContentType == artifact).Select(s => s.Content)];

    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.CSharpQuery, "Query01(IDbConnection connection)", "Query02(IDbConnection connection)")]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "Query01(DbContext ctx)", "Query02(DbContext ctx)")]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.JavaQuery, "query01(EntityManager em)", "query02(EntityManager em)")]
    public void EverySelectOfAScriptIsAQueryNamedByItsPosition(
        ORMEnum target,
        ConversionContentType artifact,
        string first,
        string second)
    {
        var result = Convert(target, $"{Rich};\n{Ordered};");

        var methods = Methods(result, artifact);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains(first, StringComparison.Ordinal) && m.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains(second, StringComparison.Ordinal) && !m.Contains("2000", StringComparison.Ordinal));

        // Only the query unit is asked about: without a catalog, a Java target refuses the
        // entity for want of a key, which is the entity's story and not the script's.
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit == "reports.sql");
    }

    /// <summary>
    /// The number is given before the reading, so a SELECT the reading refuses keeps its
    /// place: the third query stays Query03, and a later version that reads the second will
    /// not rename it. The refusal names the query by the same name it would have stood under
    /// in the output, which is the only name the user can find it by.
    /// </summary>
    [Fact]
    public void ARefusedSelectKeepsItsNumberAndDoesNotRenumberItsNeighbours()
    {
        var result = Convert(ORMEnum.Dapper, $"{Rich};\n{Document};\n{Ordered};");

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(IDbConnection connection)", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query03(IDbConnection connection)", StringComparison.Ordinal));
        Assert.DoesNotContain(methods, m => m.Contains("Query02", StringComparison.Ordinal));

        var refused = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Query02", refused.Query);
        Assert.Equal("reports.sql", refused.Unit);
        Assert.Contains("FOR clause", refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>A batch separator ends a batch, not the script: the SELECTs on both sides of GO are queries of the same unit.</summary>
    [Fact]
    public void TheScriptReadsAcrossGo()
    {
        var result = Convert(ORMEnum.Dapper, $"{Rich}\nGO\n{Ordered}\nGO\n");

        var methods = Methods(result, ConversionContentType.CSharpQuery);

        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query02(IDbConnection connection)", StringComparison.Ordinal));
    }

    /// <summary>
    /// A unit of one SELECT is not numbered: there is nothing to tell its query from, and a
    /// number would change every Dapper artifact the matrices and the samples compare.
    /// </summary>
    [Fact]
    public void ASingleSelectKeepsTheFixedName()
    {
        var result = Convert(ORMEnum.Dapper, $"{Rich};");

        var method = Assert.Single(Methods(result, ConversionContentType.CSharpQuery));

        Assert.Contains(" Query(IDbConnection connection)", method, StringComparison.Ordinal);
        Assert.All(result.Records, r => Assert.Null(r.Query));
    }

    /// <summary>
    /// A statement that is not a SELECT that reads refuses the whole unit, found before any
    /// query is read: what it writes changes what the SELECTs after it read - a temporary
    /// table, a local variable, the database itself - so the queries of the script stop
    /// being independent. A SELECT that assigns to a variable is among them, and used to
    /// come out read without its assignment, as SELECT * over the table: a different row set.
    /// </summary>
    [Theory]
    [InlineData("DELETE FROM Sales.Customers; " + Rich, "DELETE")]
    [InlineData("SELECT c.CustomerName INTO #rich FROM Sales.Customers AS c; SELECT r.CustomerName FROM #rich AS r", "SELECT … INTO")]
    [InlineData("DECLARE @floor int = 2000; SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CreditLimit > @floor", "DECLARE")]
    [InlineData("USE Archive; " + Rich, "USE")]
    [InlineData("SELECT @top = MAX(c.CreditLimit) FROM Sales.Customers AS c", "SELECT @top = …")]
    public void AStatementThatDoesNotReadRefusesTheWholeScript(string script, string named)
    {
        var builder = new DapperSqlQueryBuilder();

        var builders = new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, script);

        Assert.Same(builder, Assert.Single(builders));
        Assert.Null(builder.QueryName);
        Assert.Empty(builder.Build());

        var refused = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains(named, refused.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The literal of a Dapper call is one command, of which Query&lt;T&gt; maps the first
    /// result set and reads the rest into nothing; two methods would claim two queries where
    /// the source makes one.
    /// </summary>
    [Fact]
    public void TwoSelectsInTheLiteralOfADapperCallAreRefused()
    {
        var builder = new DapperSqlQueryBuilder();

        new DapperSqlQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $"public List<Customer> Get(IDbConnection connection) => connection.Query<Customer>(\"{Rich}; {Ordered}\").ToList();");

        AssertRefusedAsOneCommand(builder);
    }

    /// <summary>A MyBatis &lt;select&gt; maps one result set unless resultSets names more, which the tool does not read.</summary>
    [Fact]
    public void TwoSelectsInAMyBatisStatementAreRefused()
        => AssertRefusedAsOneCommand(FromMyBatisMapper($"{Rich}; {Ordered}"));

    /// <summary>A named native query of NHibernate is one query with one result.</summary>
    [Fact]
    public void TwoSelectsInANativeQueryAreRefused()
        => AssertRefusedAsOneCommand(FromNativeQuery($"{Rich}; {Ordered}"));

    /// <summary>
    /// The refusal of a statement that does not read holds in every route, the single
    /// statement too: the assignment used to come out of the shared reading as SELECT *. A
    /// MyBatis statement never gets that far - an identifier introduced by @ binds to
    /// nothing in MyBatis and the wrapper refuses it by its own reason (decision 084) -, so
    /// there the claim is the refusal alone.
    /// </summary>
    [Fact]
    public void AnAssignmentToAVariableIsRefusedInEveryRoute()
    {
        const string assignment = "SELECT @top = MAX(c.CreditLimit) FROM Sales.Customers AS c";

        var fromCall = new DapperSqlQueryBuilder();
        new DapperSqlQueryParser(() => fromCall).Parse(
            ConversionContentType.CSharpQuery,
            $"public decimal Get(IDbConnection connection) => connection.ExecuteScalar<decimal>(\"{assignment}\");");

        foreach (var builder in new[] { fromCall, FromNativeQuery(assignment) })
        {
            Assert.Empty(builder.Build());
            Assert.Contains(
                builder.Records,
                r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("SELECT @top = …", StringComparison.Ordinal));
        }

        var fromMapper = FromMyBatisMapper(assignment);
        Assert.Empty(fromMapper.Build());
        Assert.Contains(fromMapper.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Theory]
    [InlineData(1, "Query01")]
    [InlineData(9, "Query09")]
    [InlineData(99, "Query99")]
    [InlineData(100, "Query100")]
    public void ThePositionIsWrittenInTwoDigitsAndGrowsPastNinetyNine(int position, string name)
        => Assert.Equal(name, QueryMethodNaming.Positional(position));

    private static void AssertRefusedAsOneCommand(AbstractQueryBuilder builder)
    {
        Assert.Empty(builder.Build());

        var refused = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("2 SELECT statements", refused.Reason, StringComparison.Ordinal);
        Assert.Contains("one command", refused.Reason, StringComparison.Ordinal);
    }

    private static AbstractQueryBuilder FromMyBatisMapper(string sql)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        new MyBatisXmlQueryParser(() => builder, MyBatisReadingContext.For(new DummyEntityBuilder()))
            .Parse(ConversionContentType.XML, $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <mapper namespace="Shop.CustomerMapper">
                  <select id="findRich">{sql}</select>
                </mapper>
                """);

        return builder;
    }

    private static AbstractQueryBuilder FromNativeQuery(string sql)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder();

        new NHibernateXmlQueryParser(() => builder).Parse(ConversionContentType.XML, $"""
            <hibernate-mapping>
              <class name="Customer" table="Customers" schema="Sales" />
              <sql-query name="findRich">{sql}</sql-query>
            </hibernate-mapping>
            """);

        return builder;
    }
}
