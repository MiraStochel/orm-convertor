using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// DISTINCT end to end (decision 073): the representation carries it as a marker of the
/// (sub)query scope, the SQL parser reads it per SELECT, the LINQ parser reads the
/// Distinct() step, the HQL parser the word after select, and each target's projection step
/// writes the one word it spells - SELECT DISTINCT, .Distinct() after the Select, select
/// distinct. Its place is after the projection and before the slice, which decides what may
/// follow it in a LINQ chain; over a set operation the template applies the relational
/// identity instead of refusing.
/// </summary>
public class DistinctQueryTest
{
    private static EntityMap Customers() =>
        new() { Entity = new() { Name = "Customer" }, Table = "Customers", Schema = "Sales" };

    private static EntityMap Orders() =>
        new() { Entity = new() { Name = "Order" }, Table = "Orders", Schema = "Sales" };

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string linq, params EntityMap[] maps)
    {
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, linq, maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
        => builder.Build().Single(s => s.ContentType == type).Content;

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
    }

    private static void AssertBefore(string text, string first, string second)
    {
        var firstAt = text.IndexOf(first, StringComparison.Ordinal);
        var secondAt = text.IndexOf(second, StringComparison.Ordinal);

        Assert.True(firstAt >= 0, $"'{first}' is missing from:\n{text}");
        Assert.True(secondAt >= 0, $"'{second}' is missing from:\n{text}");
        Assert.True(firstAt < secondAt, $"'{first}' should precede '{second}' in:\n{text}");
    }

    // ---- Carried shapes ------------------------------------------------------------

    [Fact]
    public void LinqDistinctOverAProjectionBecomesSelectDistinct()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Select(c => c.Country).Distinct().ToList();
        }
        """;

        string expected = """
        SELECT DISTINCT c.Country AS Country
        FROM Sales.Customers AS c
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Equal(expected, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>A whole-entity Distinct() has no projection instruction to sit on; it is a flag of the scope.</summary>
    [Fact]
    public void LinqDistinctOverTheWholeEntityBecomesSelectDistinctStar()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Distinct().ToList();
        }
        """;

        string expected = """
        SELECT DISTINCT *
        FROM Sales.Customers AS c
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.Equal(expected, Sql(builder), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SqlDistinctBecomesDistinctAfterTheSelect()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT c.Country FROM Sales.Customers AS c");

        var chain = Artifact(builder, ConversionContentType.CSharpQuery);

        AssertBefore(chain, ".Select(c => new { Country = c.Country })", ".Distinct()");
    }

    [Fact]
    public void SqlDistinctBecomesSelectDistinctInHql()
    {
        var builder = ParseSql(new NHibernateHqlQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT c.Country FROM Sales.Customers AS c");

        string expected = """
        select distinct c.Country
        from Customer c
        """;

        Assert.Equal(expected, Artifact(builder, ConversionContentType.HqlQuery), ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// SQL evaluates DISTINCT before TOP, so Distinct().Take(5) is five distinct rows -
    /// written in the grammar's order, DISTINCT first.
    /// </summary>
    [Fact]
    public void DistinctWithALimitIsSelectDistinctTop()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Select(c => c.Country).Distinct().Take(5).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());

        Assert.StartsWith("SELECT DISTINCT TOP (5) c.Country", Sql(builder));
    }

    [Fact]
    public void SqlDistinctTopBecomesDistinctThenTake()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT TOP (5) c.Country FROM Sales.Customers AS c");

        AssertBefore(Artifact(builder, ConversionContentType.CSharpQuery), ".Distinct()", ".Take(5)");
    }

    /// <summary>
    /// EF Core drops an ordering that precedes a Distinct() without a row-limiting operator,
    /// so the ordering is written after it, on the projected shape.
    /// </summary>
    [Fact]
    public void AnOrderingUnderDistinctFollowsTheDistinctInLinq()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT c.Country FROM Sales.Customers AS c ORDER BY c.Country");

        var chain = Artifact(builder, ConversionContentType.CSharpQuery);

        Assert.Contains(".Distinct()\n        .OrderBy(p => p.Country)", chain);
    }

    [Fact]
    public void AnOrderingUnderAWholeEntityDistinctNamesTheRow()
    {
        var builder = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT * FROM Sales.Customers AS c ORDER BY c.Country");

        var chain = Artifact(builder, ConversionContentType.CSharpQuery);

        Assert.Contains(".Distinct()\n        .OrderBy(c => c.Country)", chain);
    }

    /// <summary>The grammar carries DISTINCT per SELECT, so an operand keeps its own.</summary>
    [Fact]
    public void DistinctInsideASetOperationOperandIsCarried()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>().Select(c => c.Country).Distinct()
                .Union(ctx.Set<Customer>().Select(c => c.Country))
                .ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        Assert.Contains("SELECT DISTINCT c.Country", sql);
        Assert.Contains("UNION", sql);
    }

    [Fact]
    public void DistinctInsideAnInSubQueryIsCarried()
    {
        const string sql = """
            SELECT c.CustomerId FROM Sales.Customers AS c
            WHERE c.CustomerId IN (SELECT DISTINCT o.CustomerId FROM Sales.Orders AS o)
            """;

        var dapper = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        Assert.Contains("IN (SELECT DISTINCT o.CustomerId FROM Sales.Orders AS o)", Sql(dapper));

        var efCore = ParseSql(new EFCoreLinqQueryBuilder { EntityMaps = [Customers(), Orders()] }, sql);
        Assert.Contains(".Select(o => o.CustomerId).Distinct().Contains(c.CustomerId)", Artifact(efCore, ConversionContentType.CSharpQuery));
    }

    // ---- Identities ----------------------------------------------------------------

    /// <summary>A UNION already returns a set, so a Distinct() over it collapses nothing.</summary>
    [Fact]
    public void DistinctAfterAUnionIsLeftOutWithARecord()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>().Union(ctx.Set<Customer>()).Distinct().ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        Assert.Contains("UNION", sql);
        Assert.DoesNotContain("DISTINCT", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Projection);
    }

    /// <summary>DISTINCT over UNION ALL is UNION - a rewrite rule Q14 permits, and one that is reported.</summary>
    [Fact]
    public void DistinctAfterAConcatBecomesAUnion()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>().Concat(ctx.Set<Customer>()).Distinct().ToList();
        }
        """;

        var dapper = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(dapper);

        Assert.Contains("UNION", sql);
        Assert.DoesNotContain("UNION ALL", sql);
        Assert.DoesNotContain("DISTINCT", sql);
        Assert.Contains(
            dapper.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.SetOperation);

        var efCore = ParseLinq(new EFCoreLinqQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var chain = Artifact(efCore, ConversionContentType.CSharpQuery);

        Assert.Contains(".Union(", chain);
        Assert.DoesNotContain(".Concat(", chain);
        Assert.DoesNotContain(".Distinct()", chain);
    }

    /// <summary>The identity folds into the left operand before the next operation takes it.</summary>
    [Fact]
    public void DistinctBetweenChainedSetOperationsFoldsIntoTheLeftOperand()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Set<Customer>().Concat(ctx.Set<Customer>()).Distinct().Except(ctx.Set<Customer>()).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers());
        var sql = Sql(builder);

        AssertBefore(sql, "UNION", "EXCEPT");
        Assert.DoesNotContain("UNION ALL", sql);
        Assert.DoesNotContain("DISTINCT", sql);
    }

    /// <summary>A single-row aggregate projection has nothing to collapse.</summary>
    [Fact]
    public void DistinctOverASingleRowAggregateIsLeftOut()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT COUNT(*) FROM Sales.Customers AS c");

        Assert.DoesNotContain("DISTINCT", Sql(builder));
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Projection);
    }

    /// <summary>The extreme of a set does not depend on duplicates, so the Distinct() goes with a record.</summary>
    [Fact]
    public void DistinctBeforeMaxIsLeftOutWithARecord()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => c.CreditLimit > ctx.Set<Order>().Distinct().Max(o => o.Total)).ToList();
        }
        """;

        var builder = ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, linq, Customers(), Orders());
        var sql = Sql(builder);

        Assert.Contains("(SELECT MAX(o.Total) FROM Sales.Orders AS o)", sql);
        Assert.DoesNotContain("DISTINCT", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Aggregation);
    }

    // ---- Refused shapes ------------------------------------------------------------

    /// <summary>
    /// A projection after the collapse returns duplicates SELECT DISTINCT would fold: the
    /// whole-entity collapse changes nothing and the Select after it brings the duplicates
    /// back, which is a different row set from a distinct projection.
    /// </summary>
    [Fact]
    public void AProjectionAfterDistinctRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Distinct().Select(c => c.Country).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()), QueryFeature.Projection);
    }

    [Fact]
    public void AGroupingAfterDistinctRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Distinct().GroupBy(c => c.Country).Select(g => new { g.Key, N = g.Count() }).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()), QueryFeature.Grouping);
    }

    /// <summary>Take(5).Distinct() collapses five rows to fewer, which is not SELECT DISTINCT TOP (5) (decision 060).</summary>
    [Fact]
    public void DistinctAfterTheSliceRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Take(5).Distinct().ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, linq, Customers()), QueryFeature.Pagination);
    }

    /// <summary>T-SQL rejects the query and LINQ cannot name the key after Distinct().</summary>
    [Fact]
    public void AnOrderingKeyOutsideTheProjectionRefusesTheArtifact()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] },
            "SELECT DISTINCT c.Country FROM Sales.Customers AS c ORDER BY c.CustomerName");

        AssertRefused(builder, QueryFeature.Ordering);
    }

    /// <summary>Count() over Distinct() is COUNT(DISTINCT ...), an aggregate the model does not carry.</summary>
    [Fact]
    public void ACountOverDistinctRefusesTheArtifact()
    {
        const string linq = """
        public void Query()
        {
            var q = ctx.Customers.Where(c => c.CreditLimit > ctx.Set<Order>().Distinct().Count()).ToList();
        }
        """;

        AssertRefused(ParseLinq(new DapperSqlQueryBuilder { EntityMaps = [Customers(), Orders()] }, linq, Customers(), Orders()), QueryFeature.Aggregation);
    }

    /// <summary>DISTINCT over EXCEPT ALL is not EXCEPT: for A = {1, 1, 2} and B = {1} one gives {1, 2}, the other {2}.</summary>
    [Fact]
    public void DistinctOverAnExceptAllRefusesTheArtifact()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Customers()] };
        builder.Push();
        builder.From("Sales.Customers", "c");
        builder.Pop();
        builder.SetOperation(SetOperationType.ExceptAll);
        builder.Push();
        builder.From("Sales.Customers", "c");
        builder.Pop();
        builder.Distinct();

        AssertRefused(builder, QueryFeature.SetOperation);
    }

    /// <summary>An aggregate over collapsed values in a filter used to be read as the aggregate over all of them.</summary>
    [Fact]
    public void ACountDistinctInAFilterRefusesTheArtifact()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.Country FROM Sales.Customers AS c
            GROUP BY c.Country
            HAVING COUNT(DISTINCT c.CustomerId) > 1
            """);

        AssertRefused(builder, QueryFeature.PostAggregationFiltering);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("COUNT(DISTINCT"));
    }

    /// <summary>In a projection the same modifier is a dropped column - a poorer artifact, the same rows.</summary>
    [Fact]
    public void ACountDistinctProjectionIsALoss()
    {
        var builder = ParseSql(new DapperSqlQueryBuilder { EntityMaps = [Customers()] }, """
            SELECT c.Country, COUNT(DISTINCT c.CustomerId) AS N FROM Sales.Customers AS c
            GROUP BY c.Country
            """);

        var sql = Sql(builder);

        Assert.DoesNotContain("COUNT", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Aggregation);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }
}
