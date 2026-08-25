using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Set operations end to end: parsers produce them (rule Q12), the Dapper and EF Core
/// builders render them, NHibernate refuses per its descriptor. Until now the emission path
/// existed but nothing outside the tests ever called it.
/// </summary>
public class SetOperationQueryTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    private static EntityMap Prospects() =>
        new() { Entity = new() { Name = "Prospect" }, Table = "Prospects", Schema = "Sales" };

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        new DapperSqlQueryParser(builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string linq, params EntityMap[] maps)
    {
        new EFCoreLinqQueryParser(builder).Parse(ConversionContentType.CSharpQuery, linq, maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

    [Fact]
    public void SqlUnionAllRoundTripsThroughDapper()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION ALL
            SELECT p.ContactName FROM Sales.Prospects AS p
            """);

        string expected = """
        SELECT c.CustomerName
        FROM Sales.Customers AS c

        UNION ALL

        SELECT p.ContactName
        FROM Sales.Prospects AS p
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// UNION and EXCEPT share a precedence level and associate left, so the chain has to come
    /// back grouped as (A UNION B) EXCEPT C - and the nested operand parenthesized, because
    /// written bare it could regroup into a different row set.
    /// </summary>
    [Fact]
    public void ChainedSetOperationsGroupLeft()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT p.ContactName FROM Sales.Prospects AS p
            EXCEPT
            SELECT b.BannedName FROM Sales.Banned AS b
            """);

        string expected = """
        (
        SELECT c.CustomerName
        FROM Sales.Customers AS c

        UNION

        SELECT p.ContactName
        FROM Sales.Prospects AS p
        )

        EXCEPT

        SELECT b.BannedName
        FROM Sales.Banned AS b
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// SQL Server evaluates INTERSECT before UNION and EXCEPT, but ScriptDom hands the chain
    /// over purely left-associated, so the parser has to regroup it or translate a different
    /// row set. The regrouped right side then nests: its own scopes open and close while the
    /// outer operation is still armed, which is the case the mark-depth bookkeeping exists
    /// for - a single armed-operation flag used to complete the outer UNION on the first
    /// inner Pop.
    /// </summary>
    [Fact]
    public void ARightNestedSetOperationKeepsItsGrouping()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT p.ContactName FROM Sales.Prospects AS p
            INTERSECT
            SELECT b.BannedName FROM Sales.Banned AS b
            """);

        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.SetOperation);

        string expected = """
        SELECT c.CustomerName
        FROM Sales.Customers AS c

        UNION

        (
        SELECT p.ContactName
        FROM Sales.Prospects AS p

        INTERSECT

        SELECT b.BannedName
        FROM Sales.Banned AS b
        )
        """;

        Assert.Equal(expected, Sql(builder), ignoreAllWhiteSpace: true, ignoreLineEndingDifferences: true);
    }

    [Theory]
    [InlineData("Union", "UNION")]
    [InlineData("Concat", "UNION ALL")]
    [InlineData("Intersect", "INTERSECT")]
    [InlineData("Except", "EXCEPT")]
    public void EachLinqSetOperationBecomesItsSqlKeyword(string method, string keyword)
    {
        var linq = $$"""
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Where(c => c.CreditLimit > 2000)
                .{{method}}(ctx.Set<Customer>().Where(c => c.CreditLimit == null))
                .ToList();
        }
        """;

        var sql = Sql(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));

        Assert.Contains(keyword, sql);
        Assert.Contains("WHERE c.CreditLimit > 2000", sql);
        Assert.Contains("WHERE c.CreditLimit IS NULL", sql);
    }

    [Fact]
    public void EFCoreRendersAUnionOfOneEntityAsATypedQueryable()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Where(c => c.CreditLimit > 2000)
                .Union(ctx.Set<Customer>().Where(c => c.CreditLimit == null))
                .ToList();
        }
        """;

        var builder = ParseLinq(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var chain = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains("IQueryable<Customer> Query(DbContext ctx)", chain);
        Assert.Contains(".Union(ctx.Set<Customer>()", chain);
    }

    /// <summary>
    /// LINQ set operations compose one element type; a whole entity against a different one
    /// cannot type-check, and emitting it anyway would only move the error into the
    /// consumer's build (decision 053).
    /// </summary>
    [Fact]
    public void EFCoreRefusesAUnionOfTwoDifferentEntities()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers(), Prospects()] }, """
            SELECT * FROM Sales.Customers
            UNION
            SELECT * FROM Sales.Prospects
            """);

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.SetOperation);
    }

    /// <summary>
    /// The descriptor says HQL cannot express a set operation, and now that parsers produce
    /// them the refusal has to hold for a real parsed input, not only for the builder API.
    /// </summary>
    [Fact]
    public void NHibernateRefusesAParsedSetOperationWithARecord()
    {
        var builder = ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT p.ContactName FROM Sales.Prospects AS p
            """);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Feature == QueryFeature.SetOperation);
    }

    /// <summary>
    /// An ORDER BY over the composed result has no slot in the representation. It only
    /// reorders the rows, so the artifact still comes out and the record says what got
    /// poorer - unlike a filter, which changes the row set.
    /// </summary>
    [Fact]
    public void AnOrderingOverASetOperationIsReportedAndStillEmitted()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT p.ContactName FROM Sales.Prospects AS p
            ORDER BY CustomerName
            """);

        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Ordering);
    }

    [Fact]
    public void AFilterAfterASetOperationRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Union(ctx.Set<Customer>())
                .Where(c => c.CreditLimit > 2000)
                .ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Filtering);
    }

    /// <summary>
    /// Pagination is not carried by the representation, and on the SQL side that used to be
    /// silent: TOP and OFFSET/FETCH disappeared without a record, unlike Take and Skip on the
    /// LINQ side (decision 004).
    /// </summary>
    [Theory]
    [InlineData("SELECT TOP (10) c.CustomerName FROM Sales.Customers AS c")]
    [InlineData("SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY")]
    public void SqlPaginationIsReportedAsALoss(string sql)
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, sql);

        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Pagination);
    }
}
