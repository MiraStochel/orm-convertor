using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The DISTINCT modifier of an aggregate, end to end (decision 102): <c>COUNT(DISTINCT x)</c>
/// travels as the flag of the projection or of the column operand, every parser reads its
/// own spelling - the word inside the function in SQL, HQL and JPQL, a one-column Select
/// collapsed before the aggregate in LINQ - and every target writes its own. The modifier
/// over the whole row, which no SQL target spells as one aggregate, is refused by the
/// template; Distinct() over the whole entity before a terminal aggregate keeps the rule of
/// decision 073 (<see cref="DistinctQueryTest"/>).
/// </summary>
public class AggregateDistinctTest
{
    private static EntityMap Customers()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var country = new Property { Name = "Country", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [id, country, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = country, ColumnName = "Country" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimit" },
            ],
        };
    }

    private static EntityMap Orders()
    {
        var customer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [customer] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [new PropertyMap { Property = customer, ColumnName = "CustomerID" }],
        };
    }

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string chain)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new EFCoreLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            $$"""
            public void Query()
            {
                var q = {{chain}}.ToList();
            }
            """,
            [Customers(), Orders()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Customers(), Orders()]);
        return builder;
    }

    private static AbstractQueryBuilder ParseJpql(AbstractQueryBuilder builder, string jpql)
    {
        builder.EntityMaps = [Customers(), Orders()];
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [Customers(), Orders()]);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return outputs.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static string Hql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.HqlQuery);

    private static string Jpql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JpqlQuery);

    private static string CSharp(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
    }

    private const string GroupedSql = """
        SELECT c.Country AS Country, COUNT(DISTINCT c.CustomerID) AS N
        FROM Sales.Customers AS c
        GROUP BY c.Country
        """;

    // ---- the modifier in a grouped projection --------------------------------------

    [Fact]
    public void SqlCountDistinctInAGroupedProjectionReachesEveryTarget()
    {
        Assert.Contains("COUNT(DISTINCT c.CustomerID) AS N", Sql(ParseSql(new DapperSqlQueryBuilder(), GroupedSql)));
        Assert.Contains("count(distinct c.CustomerID) as N", Hql(ParseSql(new NHibernateHqlQueryBuilder(), GroupedSql)));
        Assert.Contains("count(distinct c.CustomerID) as N", Jpql(ParseSql(new HibernateJpqlQueryBuilder(), GroupedSql)));

        var efCore = ParseSql(new EFCoreLinqQueryBuilder(), GroupedSql);
        var linq = CSharp(efCore);
        Assert.Contains(".Distinct().Count()", linq);
        Assert.Contains("Select(", linq);

        // Select(...).Distinct().Count() counts distinct non-null values, exactly as
        // COUNT(DISTINCT x) does, so the record Count() gets for COUNT(x) has no place here.
        Assert.DoesNotContain(efCore.Records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Aggregation);
    }

    [Fact]
    public void SqlCountDistinctInHavingIsCarried()
    {
        const string sql = """
            SELECT c.Country AS Country
            FROM Sales.Customers AS c
            GROUP BY c.Country
            HAVING COUNT(DISTINCT c.CustomerID) > 1
            """;

        Assert.Contains("HAVING COUNT(DISTINCT c.CustomerID) > 1", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("having count(distinct c.CustomerID) > 1", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
        Assert.Contains(".Distinct().Count() > 1", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    [Fact]
    public void LinqSelectDistinctCountOverAGroupBecomesCountDistinct()
    {
        const string chain = """
            ctx.Customers
                .GroupBy(c => c.Country)
                .Where(g => g.Select(x => x.CustomerID).Distinct().Count() > 1)
                .Select(g => new { Country = g.Key, N = g.Select(x => x.CustomerID).Distinct().Count() })
            """;

        var sql = Sql(ParseLinq(new DapperSqlQueryBuilder(), chain));

        Assert.Contains("COUNT(DISTINCT c.CustomerID) AS N", sql);
        Assert.Contains("HAVING COUNT(DISTINCT c.CustomerID) > 1", sql);
    }

    /// <summary>A one-column Select before the aggregate names its column even without the collapse.</summary>
    [Fact]
    public void LinqSelectThenSumOverAGroupIsThePlainAggregate()
    {
        const string chain = """
            ctx.Customers
                .GroupBy(c => c.Country)
                .Select(g => new { Country = g.Key, Limit = g.Select(x => x.CreditLimit).Sum() })
            """;

        Assert.Contains("SUM(c.CreditLimit) AS Limit", Sql(ParseLinq(new DapperSqlQueryBuilder(), chain)));
    }

    [Fact]
    public void HqlAndJpqlReadTheModifierAndRoundTrip()
    {
        const string hql = "select c.Country as Country, count(distinct c.CustomerID) as N from Customer c group by c.Country";
        const string jpql = "select c.Country as Country, count(distinct c.CustomerID) as N from Customer c group by c.Country";

        Assert.Contains("COUNT(DISTINCT c.CustomerID) AS N", Sql(ParseHql(new DapperSqlQueryBuilder(), hql)));
        Assert.Contains("count(distinct c.CustomerID) as N", Hql(ParseHql(new NHibernateHqlQueryBuilder(), hql)));
        Assert.Contains("COUNT(DISTINCT c.CustomerID) AS N", Sql(ParseJpql(new DapperSqlQueryBuilder(), jpql)));
        Assert.Contains("count(distinct c.CustomerID) as N", Jpql(ParseJpql(new HibernateJpqlQueryBuilder(), jpql)));
    }

    // ---- the modifier in a scalar subquery -------------------------------------------

    [Fact]
    public void AScalarSubQueryOverDistinctValuesIsCarriedBothWays()
    {
        const string sql = "SELECT * FROM Sales.Customers AS c WHERE c.CreditLimit > (SELECT COUNT(DISTINCT o.CustomerID) FROM Sales.Orders AS o)";
        Assert.Contains(".Select(o => o.CustomerID).Distinct().Count()", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
        Assert.Contains("(select count(distinct o.CustomerID) from Order o)", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));

        const string chain = "ctx.Customers.Where(c => c.CreditLimit > ctx.Set<Order>().Select(o => o.CustomerID).Distinct().Count())";
        Assert.Contains("(SELECT COUNT(DISTINCT o.CustomerID) FROM Sales.Orders AS o)", Sql(ParseLinq(new DapperSqlQueryBuilder(), chain)));
    }

    /// <summary>The extreme of a set does not depend on duplicates, yet the source wrote it and every target spells it (rule Q15).</summary>
    [Fact]
    public void MinOverDistinctValuesIsCarriedVerbatim()
    {
        const string sql = "SELECT MIN(DISTINCT c.CreditLimit) AS M FROM Sales.Customers AS c";

        Assert.Contains("MIN(DISTINCT c.CreditLimit) AS M", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("min(distinct c.CreditLimit) as M", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
    }

    // ---- the stated limit -------------------------------------------------------------

    /// <summary>count(distinct c) counts distinct rows, which no SQL target spells as one aggregate.</summary>
    [Fact]
    public void CountDistinctOverTheWholeRowIsRefusedByTheGate()
    {
        var builder = ParseJpql(new DapperSqlQueryBuilder(), "select count(distinct c) from Customer c");

        AssertRefused(builder, QueryFeature.Aggregation);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("COUNT(DISTINCT *)"));
    }

    [Fact]
    public void TheModifierWithoutAFunctionIsNotConstructible()
    {
        Assert.Throws<ArgumentException>(() => Model.QueryInstructions.Conditions.QueryOperand.Column("c", "CustomerID", null, distinct: true));
    }
}
