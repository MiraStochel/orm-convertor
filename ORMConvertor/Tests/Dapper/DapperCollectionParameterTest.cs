using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Dapper;

/// <summary>
/// Dapper's own spelling of a list parameter, read (decision 106). Dapper documents
/// <c>IN @ids</c> - a bare parameter after IN, which it expands into a list before the
/// statement reaches the server - and the shared T-SQL writer has written it that way since
/// decision 083; what was missing was the way back. The grammar is a grammar of T-SQL, to
/// which the bare form is a syntax error and <c>IN (@ids)</c> a list of one bound value
/// (decision 102), so the Dapper wrapper peels its word off the text before the grammar
/// sees it: parentheses in the text, and the collection flag beside it, the way the MyBatis
/// wrapper carries a <c>&lt;foreach&gt;</c> (decision 084).
/// </summary>
public class DapperCollectionParameterTest
{
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, name] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
            ],
        };
    }

    private static AbstractQueryBuilder Parse(ConversionContentType contentType, string source)
    {
        AbstractQueryBuilder builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        new DapperSqlQueryParser(() => builder).Parse(contentType, source);
        return builder;
    }

    private static AbstractQueryBuilder ParseSql(string sql) => Parse(ConversionContentType.SqlQuery, sql);

    /// <summary>Every query of a unit, each with a builder of its own, in the order of the text.</summary>
    private static List<AbstractQueryBuilder> ParseAll(ConversionContentType contentType, string source)
        => [.. new DapperSqlQueryParser(() => new DapperSqlQueryBuilder { EntityMaps = [Customers()] }).Parse(contentType, source)];

    /// <summary>Two queries of one unit: the first binds a list to ids, the second one value.</summary>
    private static void AssertListThenValue(List<AbstractQueryBuilder> builders)
    {
        Assert.Equal(2, builders.Count);
        Assert.Contains("Query01(IDbConnection connection, IEnumerable<int> ids)", CSharp(builders[0]));
        Assert.Contains("Query02(IDbConnection connection, int ids)", CSharp(builders[1]));
        Assert.All(builders, builder => Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure));
    }

    private static string? Sql(AbstractQueryBuilder builder)
        => builder.Build().SingleOrDefault(s => s.ContentType == ConversionContentType.SqlQuery)?.Content;

    private static string? CSharp(AbstractQueryBuilder builder)
        => builder.Build().SingleOrDefault(s => s.ContentType == ConversionContentType.CSharpQuery)?.Content;

    /// <summary>
    /// The bare form out of a bare SQL unit: a collection parameter whose scalar comes from
    /// the column, so the generated method takes the sequence Dapper expands.
    /// </summary>
    [Fact]
    public void ABareParameterAfterInIsACollectionParameter()
    {
        var builder = ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids");

        Assert.Contains("WHERE c.CustomerID IN @ids", Sql(builder));
        Assert.Contains("IEnumerable<int> ids", CSharp(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// The same step on the other route in: the literal pulled out of a Dapper call. The call
    /// itself is not read - <c>new { ids }</c> says nothing here - because the fact is in the
    /// SQL text (decision 106).
    /// </summary>
    [Fact]
    public void ABareParameterAfterInIsReadOutOfADapperCallToo()
    {
        var builder = Parse(
            ConversionContentType.CSharp,
            """
            public IEnumerable<Customer> Find(IDbConnection connection, IEnumerable<int> ids)
            {
                return connection.Query<Customer>("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids", new { ids });
            }
            """);

        Assert.Contains("WHERE c.CustomerID IN @ids", Sql(builder));
        Assert.Contains("IEnumerable<int> ids", CSharp(builder));
    }

    [Fact]
    public void ABareParameterAfterNotInIsTheNegation()
    {
        var builder = ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID NOT IN @ids");

        // The writer spells a negation as NOT over the predicate (decision 074).
        Assert.Contains("WHERE NOT (c.CustomerID IN @ids)", Sql(builder));
        Assert.Contains("IEnumerable<int> ids", CSharp(builder));
    }

    /// <summary>
    /// A comment between the keyword and the parameter is trivia to the lexer and hides
    /// nothing: the form is recognized over tokens, not over the text.
    /// </summary>
    [Fact]
    public void TriviaBetweenInAndTheParameterDoesNotHideTheForm()
    {
        var builder = ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN /* the ids */ @ids");

        Assert.Contains("WHERE c.CustomerID IN @ids", Sql(builder));
        Assert.Contains("IEnumerable<int> ids", CSharp(builder));
    }

    /// <summary>
    /// The parenthesized form stays what decision 102 made it: a list of one bound value,
    /// and the method takes one value. It is the truth about Dapper - a parameter in
    /// parentheses is a single value to it, and a list bound there fails - so the tool reads
    /// the query Dapper runs rather than the one the caller may have meant.
    /// </summary>
    [Fact]
    public void AParameterInParenthesesStaysOneBoundValue()
    {
        var builder = ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN (@ids)");

        Assert.Contains("WHERE c.CustomerID IN (@ids)", Sql(builder));
        Assert.Contains("(IDbConnection connection, int ids)", CSharp(builder));
    }

    /// <summary>
    /// A string literal is one token to the lexer, so the word inside it is left alone and
    /// no parameter comes of it.
    /// </summary>
    [Fact]
    public void TheFormInsideAStringLiteralIsLeftAlone()
    {
        var builder = ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerName = 'IN @x'");

        Assert.Contains("WHERE c.CustomerName = 'IN @x'", Sql(builder));
        Assert.Contains("(IDbConnection connection)", CSharp(builder));
    }

    /// <summary>
    /// One name standing bare after one IN and in parentheses after another is one name for
    /// two bindings, the refusal the gate of the builder template makes for a name bound
    /// once as a list and once as a value - made by the wrapper here, because the facts it
    /// hands the grammar travel per name and the grammar could not tell the two apart.
    /// </summary>
    [Fact]
    public void TheSameNameBareAndInParenthesesRefusesTheArtifact()
    {
        var builder = ParseSql(
            "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids AND c.CustomerID IN (@ids)");

        var refusal = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(QueryFeature.QueryParameter, refusal.Feature);
        Assert.Contains("one name for two bindings", refusal.Reason);
        Assert.Empty(builder.Build());
    }

    /// <summary>
    /// The same name bare after IN and as a scalar elsewhere reaches the gate as two
    /// bindings and is refused there, as before.
    /// </summary>
    [Fact]
    public void TheSameNameBareAndAsAScalarRefusesTheArtifact()
    {
        var builder = ParseSql(
            "SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids AND c.CustomerID = @ids");

        // The gate runs in the template, so the refusal is made when the query is built.
        Assert.Empty(builder.Build());
        var refusal = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(QueryFeature.QueryParameter, refusal.Feature);
        Assert.Contains("one name for two bindings", refusal.Reason);
    }

    /// <summary>
    /// The facts belong to one query, not to the text they were peeled off. A bare unit is a
    /// script whose every SELECT is a query with a method and a parameter of its own
    /// (decision 108), so the same name bare in one SELECT and in parentheses in the next is
    /// two parameters of two methods. The step used to stand over the whole text and refused
    /// the unit as one name for two bindings.
    /// </summary>
    [Fact]
    public void TheFactsOfAScriptAreTheFactsOfEachQuery()
    {
        var builders = ParseAll(
            ConversionContentType.SqlQuery,
            """
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids;
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN (@ids);
            """);

        AssertListThenValue(builders);
    }

    /// <summary>
    /// The text of QueryMultiple is a script as much as a bare unit (decision 109), and its
    /// SELECTs get the facts of their own span the same way.
    /// </summary>
    [Fact]
    public void TheFactsOfQueryMultipleAreTheFactsOfEachQuery()
    {
        var builders = ParseAll(
            ConversionContentType.CSharp,
            """
            public void Load(IDbConnection connection, IEnumerable<int> ids, int id)
            {
                using var grid = connection.QueryMultiple("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids; SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN (@ids)", new { ids });
            }
            """);

        AssertListThenValue(builders);
    }

    /// <summary>
    /// One name for two bindings is still refused within one query, and it refuses that query
    /// alone: its neighbour in the script comes out with the list it states.
    /// </summary>
    [Fact]
    public void OneNameForTwoBindingsRefusesItsOwnQueryAlone()
    {
        var builders = ParseAll(
            ConversionContentType.SqlQuery,
            """
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids AND c.CustomerID IN (@ids);
            SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids;
            """);

        Assert.Equal(2, builders.Count);

        var refusal = Assert.Single(builders[0].Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("one name for two bindings", refusal.Reason);
        Assert.Empty(builders[0].Build());

        Assert.Contains("Query02(IDbConnection connection, IEnumerable<int> ids)", CSharp(builders[1]));
        Assert.DoesNotContain(builders[1].Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// Reading catches up with writing: what the T-SQL writer has emitted for a collection
    /// parameter since decision 083 is read back into the same query, so Dapper → Dapper
    /// returns the text unchanged.
    /// </summary>
    [Fact]
    public void DapperToDapperReturnsTheBareFormUnchanged()
    {
        var first = Sql(ParseSql("SELECT c.CustomerName FROM Sales.Customers AS c WHERE c.CustomerID IN @ids"));
        Assert.NotNull(first);
        Assert.Contains("IN @ids", first);

        var second = Sql(ParseSql(first));

        Assert.Equal(first, second);
    }
}
