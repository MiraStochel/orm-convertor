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
/// Pagination end to end (decision 060): the representation carries offset-then-limit, the
/// SQL parser reads TOP and OFFSET/FETCH, the LINQ parser reads Skip and Take, and each
/// target places the slice where its own surface takes it - TOP inside the SELECT clause,
/// OFFSET/FETCH after the ordering, Skip/Take at the end of the chain, and
/// SetFirstResult/SetMaxResults on the IQuery outside the HQL text. What cannot be carried
/// refuses the artifact, because a query emitted without its pagination returns a different
/// set of rows.
/// </summary>
public class PaginationQueryTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

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

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
        => builder.Build().Single(s => s.ContentType == type).Content;

    private static void AssertRefused(AbstractQueryBuilder builder)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Pagination);
    }

    // ---- Carried shapes ------------------------------------------------------------

    [Fact]
    public void LinqSkipAndTakeBecomeOffsetFetch()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.OrderBy(c => c.CustomerName).Skip(10).Take(5).ToList();
        }
        """;

        string expected = """
        SELECT *
        FROM Sales.Customers AS c
        ORDER BY c.CustomerName ASC
        OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Equal(expected, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void LinqTakeAloneBecomesTop()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Take(5).ToList();
        }
        """;

        string expected = """
        SELECT TOP (5) *
        FROM Sales.Customers AS c
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Equal(expected, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// T-SQL does not write OFFSET without an ORDER BY. The carrier orders by nothing, so it
    /// asserts no fact the source did not state - and it is reported as the convention it is.
    /// </summary>
    [Fact]
    public void SkipWithoutOrderingGetsAnOrderNeutralCarrier()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Skip(10).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        Assert.Contains("ORDER BY (SELECT NULL)", sql);
        Assert.Contains("OFFSET 10 ROWS", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Pagination);
    }

    [Fact]
    public void SqlOffsetFetchRoundTripsThroughDapper()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY
            """);

        string expected = """
        SELECT c.CustomerName
        FROM Sales.Customers AS c
        ORDER BY c.CustomerName ASC
        OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY
        """;

        Assert.Equal(expected, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SqlTopBecomesTake()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT TOP (10) c.CustomerName FROM Sales.Customers AS c");

        Assert.Contains(".Take(10)", Artifact(builder, ConversionContentType.CSharpQuery));
    }

    [Fact]
    public void SqlOffsetFetchBecomesSkipAndTake()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY
            """);

        var chain = Artifact(builder, ConversionContentType.CSharpQuery);

        Assert.Contains(".Skip(10)", chain);
        Assert.Contains(".Take(5)", chain);
    }

    /// <summary>
    /// NHibernate 5.7.0 has no limit or offset in HQL: the slice lives on the IQuery the
    /// method returns. The bare HQL artifact not carrying it is a property of the format,
    /// so there is no record about it (the reasoning of decision 028).
    /// </summary>
    [Fact]
    public void SkipAndTakeReachTheNHibernateQueryMethod()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.OrderBy(c => c.CustomerName).Skip(10).Take(5).ToList();
        }
        """;

        var builder = ParseLinq(new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var artifacts = builder.Build();

        var method = artifacts.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        var hql = artifacts.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;

        Assert.Contains(".SetFirstResult(10)", method);
        Assert.Contains(".SetMaxResults(5)", method);
        Assert.DoesNotContain("SetFirstResult", hql);
        Assert.DoesNotContain("SetMaxResults", hql);
        Assert.DoesNotContain(builder.Records, r => r.Feature == QueryFeature.Pagination);
    }

    // ---- Set operations ------------------------------------------------------------

    /// <summary>TOP is legal inside a set operation operand, so it is carried there.</summary>
    [Fact]
    public void ATopInsideASetOperationOperandIsCarried()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Take(5)
                .Union(ctx.Set<Customer>())
                .ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        Assert.Contains("TOP (5)", sql);
        Assert.Contains("UNION", sql);
    }

    /// <summary>T-SQL has no way to write OFFSET/FETCH inside a set operation operand.</summary>
    [Fact]
    public void AnOffsetInsideASetOperationOperandRefusesTheSqlTarget()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Union(ctx.Set<Customer>().Skip(5))
                .ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));
    }

    [Fact]
    public void PaginationAfterASetOperationRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>()
                .Union(ctx.Set<Customer>())
                .Take(5)
                .ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));
    }

    [Fact]
    public void ATrailingOffsetOverASqlSetOperationRefusesTheArtifact()
    {
        AssertRefused(ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT p.ContactName FROM Sales.Prospects AS p
            ORDER BY CustomerName OFFSET 10 ROWS
            """));
    }

    // ---- Refused shapes ------------------------------------------------------------

    [Theory]
    [InlineData("SELECT TOP (10) PERCENT c.CustomerName FROM Sales.Customers AS c")]
    [InlineData("SELECT TOP (10) WITH TIES c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName")]
    [InlineData("SELECT TOP (@n) c.CustomerName FROM Sales.Customers AS c")]
    [InlineData("SELECT c.CustomerName FROM Sales.Customers AS c ORDER BY c.CustomerName OFFSET @skip ROWS")]
    public void SqlPaginationTheRepresentationCannotCarryRefusesTheArtifact(string sql)
        => AssertRefused(ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, sql));

    [Fact]
    public void ANonLiteralTakeRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Take(pageSize).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));
    }

    /// <summary>
    /// Take-then-Skip slices differently from the offset-then-limit normal form, and
    /// rewriting the arithmetic silently would be a reinterpretation, not a translation.
    /// </summary>
    [Fact]
    public void SkipAfterTakeRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Take(5).Skip(2).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));
    }

    /// <summary>
    /// A filter after the slice selects different rows than a filter before it, and the
    /// representation carries pagination only as the last operation.
    /// </summary>
    [Fact]
    public void AFilterAfterTheSliceRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Skip(5).Where(c => c.CreditLimit > 100).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()));
    }

    /// <summary>Projection commutes with the slice, so it may follow it.</summary>
    [Fact]
    public void AProjectionAfterTheSliceIsCarried()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.OrderBy(c => c.CustomerName).Skip(10).Take(5).Select(c => c.CustomerName).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        Assert.Contains("SELECT c.CustomerName", sql);
        Assert.Contains("OFFSET 10 ROWS FETCH NEXT 5 ROWS ONLY", sql);
    }
}
