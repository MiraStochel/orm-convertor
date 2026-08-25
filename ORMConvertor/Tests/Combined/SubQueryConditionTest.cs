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
/// Subqueries as condition operands, end to end (decision 061): the operand carries a
/// nested query, EXISTS is an operator the way IS NULL is, and the only position that
/// renders is a WHERE or HAVING operand - IN over a one-column subquery, EXISTS over any,
/// a scalar comparison over a single aggregate. What a target cannot render faithfully
/// refuses the artifact, because a query emitted without its subquery condition returns a
/// different set of rows.
/// </summary>
public class SubQueryConditionTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    private static EntityMap Orders() =>
        new() { Entity = new() { Name = "Order" }, Table = "Orders", Schema = "Sales" };

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

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
        => builder.Build().Single(s => s.ContentType == type).Content;

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
    }

    // ---- Carried shapes ------------------------------------------------------------

    [Fact]
    public void SqlInSubQueryRoundTripsToSql()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT o.CustomerID FROM Sales.Orders AS o WHERE o.Total > 500)
        """;

        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);

        Assert.Equal(
            sql,
            Artifact(builder, ConversionContentType.SqlQuery),
            ignoreWhiteSpaceDifferences: true,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SqlCorrelatedExistsBecomesLinqAny()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE EXISTS (SELECT o.OrderID FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)
        """;

        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        var method = Artifact(builder, ConversionContentType.CSharpQuery);

        Assert.Contains(
            ".Where(c => ctx.Set<Order>().Where(o => o.CustomerID == c.CustomerID).Any())",
            method);
    }

    [Fact]
    public void SqlNotExistsBecomesHqlNotExists()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE NOT EXISTS (SELECT o.OrderID FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)
        """;

        var builder = ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        var hql = Artifact(builder, ConversionContentType.HqlQuery);

        Assert.Contains(
            "where not (exists (select o.OrderID from Order o where o.CustomerID = c.CustomerID))",
            hql);
    }

    [Fact]
    public void LinqContainsBecomesSqlIn()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => ctx.Orders.Select(o => o.CustomerID).Contains(c.CustomerID))
                .ToList();
        }
        """;

        const string expected = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT o.CustomerID AS CustomerID FROM Sales.Orders AS o)
        """;

        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] },
            linq,
            Customers(),
            Orders());

        Assert.Equal(
            expected,
            Artifact(builder, ConversionContentType.SqlQuery),
            ignoreWhiteSpaceDifferences: true,
            ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void LinqAnyWithPredicateBecomesSqlExists()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => ctx.Orders.Any(o => o.CustomerID == c.CustomerID))
                .ToList();
        }
        """;

        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] },
            linq,
            Customers(),
            Orders());

        Assert.Contains(
            "WHERE EXISTS (SELECT * FROM Sales.Orders AS o WHERE o.CustomerID = c.CustomerID)",
            Artifact(builder, ConversionContentType.SqlQuery));
    }

    [Fact]
    public void SqlScalarMaxBecomesLinqTerminalAggregate()
    {
        const string sql = """
        SELECT *
        FROM Sales.Orders AS o
        WHERE o.Total > (SELECT MAX(o2.Total) FROM Sales.Orders AS o2)
        """;

        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Orders()] }, sql);
        var method = Artifact(builder, ConversionContentType.CSharpQuery);

        Assert.Contains(".Where(o => o.Total > ctx.Set<Order>().Max(o2 => o2.Total))", method);
    }

    [Fact]
    public void LinqScalarMaxBecomesSqlScalarSubQuery()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Orders
                .Where(o => o.Total >= ctx.Orders.Max(m => m.Total))
                .ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Orders()] }, linq, Orders());

        Assert.Contains(
            "WHERE o.Total >= (SELECT MAX(o.Total) FROM Sales.Orders AS o)",
            Artifact(builder, ConversionContentType.SqlQuery));
    }

    [Fact]
    public void SqlScalarSubQueryBecomesHql()
    {
        const string sql = """
        SELECT *
        FROM Sales.Orders AS o
        WHERE o.Total > (SELECT AVG(o2.Total) FROM Sales.Orders AS o2)
        """;

        var builder = ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Orders()] }, sql);

        Assert.Contains(
            "where o.Total > (select avg(o2.Total) from Order o2)",
            Artifact(builder, ConversionContentType.HqlQuery));
    }

    /// <summary>
    /// A subquery over the outer query's own table: C# forbids the nested lambda from
    /// reusing the outer parameter, so the nested alias comes from the chain's own lambda
    /// and the correlated reference keeps naming the outer scope.
    /// </summary>
    [Fact]
    public void CorrelatedSubQueryOverTheSameTableKeepsBothScopesApart()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => ctx.Customers.Any(x => x.CreditLimit > c.CreditLimit))
                .ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Contains(
            "WHERE EXISTS (SELECT * FROM Sales.Customers AS x WHERE x.CreditLimit > c.CreditLimit)",
            Artifact(builder, ConversionContentType.SqlQuery));
    }

    /// <summary>
    /// Pagination inside a subquery goes where the target's grammar takes it (decision 060's
    /// sentence): T-SQL writes TOP inside the operand, LINQ writes Take.
    /// </summary>
    [Fact]
    public void PaginationInsideASubQueryIsCarriedWhereTheGrammarAllows()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT TOP (5) o.CustomerID FROM Sales.Orders AS o)
        """;

        var dapper = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        Assert.Contains(
            "IN (SELECT TOP (5) o.CustomerID FROM Sales.Orders AS o)",
            Artifact(dapper, ConversionContentType.SqlQuery));

        var efcore = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        Assert.Contains(
            ".Where(c => ctx.Set<Order>().Select(o => o.CustomerID).Take(5).Contains(c.CustomerID))",
            Artifact(efcore, ConversionContentType.CSharpQuery));
    }

    /// <summary>
    /// An ordering inside a subquery does not change which rows the outer query returns,
    /// and T-SQL does not even allow it there without TOP or OFFSET - dropped with a record.
    /// </summary>
    [Fact]
    public void OrderingInsideASubQueryIsDroppedWithALoss()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => ctx.Orders.OrderBy(o => o.Total).Select(o => o.CustomerID).Contains(c.CustomerID))
                .ToList();
        }
        """;

        var builder = ParseLinq(
            new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] },
            linq,
            Customers(),
            Orders());

        var sql = Artifact(builder, ConversionContentType.SqlQuery);

        Assert.Contains("IN (SELECT o.CustomerID AS CustomerID FROM Sales.Orders AS o)", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Ordering);
    }

    // ---- Refused shapes ------------------------------------------------------------

    [Fact]
    public void InSubQueryProjectingTwoColumnsRefuses()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT o.CustomerID, o.OrderID FROM Sales.Orders AS o)
        """;

        AssertRefused(
            ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql),
            QueryFeature.Subquery);
    }

    [Fact]
    public void InSubQueryProjectingNoColumnRefuses()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT * FROM Sales.Orders AS o)
        """;

        AssertRefused(
            ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql),
            QueryFeature.Subquery);
    }

    [Fact]
    public void SetOperationAsSubQueryBodyRefuses()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT o.CustomerID FROM Sales.Orders AS o UNION SELECT o2.CustomerID FROM Sales.Orders AS o2)
        """;

        AssertRefused(
            ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql),
            QueryFeature.Subquery);
    }

    [Fact]
    public void SubQueryInAJoinOnConditionRefuses()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        INNER JOIN Sales.Orders AS o ON o.CustomerID = c.CustomerID AND EXISTS (SELECT x.OrderID FROM Sales.Orders AS x)
        """;

        AssertRefused(
            ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql),
            QueryFeature.Subquery);
    }

    /// <summary>
    /// A scalar subquery that is not a single ungrouped aggregate has no faithful LINQ
    /// form: First() would silently pick one row where SQL refuses several.
    /// </summary>
    [Fact]
    public void EFCoreRefusesAScalarSubQueryThatIsNoAggregate()
    {
        const string sql = """
        SELECT *
        FROM Sales.Orders AS o
        WHERE o.Total > (SELECT o2.Total FROM Sales.Orders AS o2 WHERE o2.OrderID = 1)
        """;

        AssertRefused(
            ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Orders()] }, sql),
            QueryFeature.Subquery);

        // The same shape is native T-SQL and stays carried on the SQL side.
        var dapper = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Orders()] }, sql);
        Assert.Contains(
            "WHERE o.Total > (SELECT o2.Total FROM Sales.Orders AS o2 WHERE o2.OrderID = 1)",
            Artifact(dapper, ConversionContentType.SqlQuery));
    }

    /// <summary>
    /// HQL text has no place for a pagination, and SetFirstResult/SetMaxResults cannot reach
    /// inside a subquery - the sentence decision 060 said for set-operation operands.
    /// </summary>
    [Fact]
    public void NHibernateRefusesPaginationInsideASubQuery()
    {
        const string sql = """
        SELECT *
        FROM Sales.Customers AS c
        WHERE c.CustomerID IN (SELECT TOP (5) o.CustomerID FROM Sales.Orders AS o)
        """;

        AssertRefused(
            ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql),
            QueryFeature.Pagination);
    }
}
