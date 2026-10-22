using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using MyBatisWrappers;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A subquery in the condition of a join (decision 162): an operand there as in a WHERE -
/// <c>IN (SELECT …)</c>, <c>NOT IN</c>, <c>EXISTS</c>, a correlated one -, in an inner and in
/// a left join, which every target writes in its own language: T-SQL as written, HQL 7.4 and
/// JPQL after <c>on</c>, HQL 5.7 after <c>with</c>, LINQ as the correlated SelectMany a
/// condition beyond key equalities takes (decision 113), with <c>Contains</c> and <c>Any</c>
/// inside its predicate. Each spelling is read back as the same query. What the pinned
/// releases make of the spellings is measured beside the hooks: <c>NHibernateHqlProviderTest</c>,
/// <c>EFCoreJoinSubqueryProviderTest</c> and the two claims tests of the Java suite.
/// </summary>
public class JoinConditionSubqueryTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var map = new EntityMap { Entity = new Entity { Name = entity }, Table = table };
        foreach (var (name, type) in columns)
        {
            var property = new Property { Name = name, Type = LangType.Scalar(type) };
            map.Entity.Properties.Add(property);
            map.PropertyMaps.Add(new PropertyMap { Property = property, ColumnName = name });
        }

        return map;
    }

    private static List<EntityMap> Maps() =>
    [
        Map("Customer", "Customers", ("CustomerId", ScalarType.Int), ("Name", ScalarType.String)),
        Map("Purchase", "Purchases", ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int), ("Total", ScalarType.Decimal), ("Rating", ScalarType.Int)),
    ];

    private static AbstractQueryBuilder Builder(ORMEnum target) => target switch
    {
        ORMEnum.Dapper => new DapperSqlQueryBuilder(),
        ORMEnum.MyBatis => new MyBatisSqlQueryBuilder(),
        ORMEnum.EFCore => new EFCoreLinqQueryBuilder(),
        ORMEnum.NHibernate => new NHibernateHqlQueryBuilder(),
        ORMEnum.Hibernate => new HibernateJpqlQueryBuilder(),
        ORMEnum.EclipseLink => new EclipseLinkJpqlQueryBuilder(),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static AbstractQueryBuilder Sql(ORMEnum target, string sql)
    {
        var builder = Builder(target);
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, Maps());
        return builder;
    }

    private static AbstractQueryBuilder Jpql(ORMEnum target, string jpql, bool eclipseLink = false)
    {
        var builder = Builder(target);
        builder.EntityMaps = Maps();
        JakartaPersistence.JpqlQueryParser parser = eclipseLink
            ? new EclipseLinkJpqlQueryParser(() => builder)
            : new HibernateJpqlQueryParser(() => builder);
        parser.Parse(ConversionContentType.JpqlQuery, jpql, Maps());
        return builder;
    }

    private static AbstractQueryBuilder Hql(ORMEnum target, string hql)
    {
        var builder = Builder(target);
        builder.EntityMaps = Maps();
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, Maps());
        return builder;
    }

    private static AbstractQueryBuilder Linq(ORMEnum target, string chain)
    {
        var builder = Builder(target);
        builder.EntityMaps = Maps();
        var source = $$"""
            public void Query()
            {
                var q = ({{chain}}).ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, Maps());
        return builder;
    }

    /// <summary>The query in the target's own language: an artifact, and no record of a failure or a fallback.</summary>
    private static string Text(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);

        var query = built.FirstOrDefault(s => s.ContentType is ConversionContentType.SqlQuery or ConversionContentType.HqlQuery or ConversionContentType.JpqlQuery)
            ?? built.First();
        return OneLine(query.Content);
    }

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    private const string InOverASubquery =
        "SELECT c.Name AS Name, o.OrderId AS OrderId FROM Customers AS c INNER JOIN Purchases o ON o.CustomerId = c.CustomerId AND c.CustomerId IN (SELECT r.CustomerId FROM Purchases AS r WHERE r.Rating > 3)";

    private const string NotInOverACorrelatedSubqueryInALeftJoin =
        "SELECT c.Name AS Name, o.OrderId AS OrderId FROM Customers AS c LEFT JOIN Purchases o ON o.CustomerId = c.CustomerId AND NOT (c.CustomerId IN (SELECT r.CustomerId FROM Purchases AS r WHERE r.CustomerId = c.CustomerId AND r.Rating > 3))";

    private const string ExistsInAJoin =
        "SELECT c.Name AS Name, o.OrderId AS OrderId FROM Customers AS c INNER JOIN Purchases o ON o.CustomerId = c.CustomerId AND EXISTS (SELECT r.OrderId FROM Purchases AS r WHERE r.CustomerId = c.CustomerId AND r.Rating > 3)";

    // ---- T-SQL in, every target out ------------------------------------------------------

    [Theory]
    [InlineData(InOverASubquery)]
    [InlineData(NotInOverACorrelatedSubqueryInALeftJoin)]
    [InlineData(ExistsInAJoin)]
    public void TSqlWritesTheSubqueryWhereTheSourceWroteIt(string sql)
    {
        Assert.True(sql == Text(Sql(ORMEnum.Dapper, sql)), Text(Sql(ORMEnum.Dapper, sql)));

    }

    [Fact]
    public void HqlAndJpqlWriteTheSubqueryAfterOn()
    {
        const string hql = "select c.Name as Name, o.OrderId as OrderId from Customer c join Purchase o on (o.CustomerId = c.CustomerId and c.CustomerId in (select r.CustomerId from Purchase r where r.Rating > 3))";

        Assert.True(hql == Text(Sql(ORMEnum.Hibernate, InOverASubquery)), Text(Sql(ORMEnum.Hibernate, InOverASubquery)));
        Assert.True(hql == Text(Sql(ORMEnum.EclipseLink, InOverASubquery)), Text(Sql(ORMEnum.EclipseLink, InOverASubquery)));

        Assert.True(Text(Sql(ORMEnum.Hibernate, NotInOverACorrelatedSubqueryInALeftJoin)).Contains("left join Purchase o on (o.CustomerId = c.CustomerId and not (c.CustomerId in (select r.CustomerId from Purchase r where r.CustomerId = c.CustomerId and r.Rating > 3)))", StringComparison.Ordinal), Text(Sql(ORMEnum.Hibernate, NotInOverACorrelatedSubqueryInALeftJoin)));
        Assert.True(Text(Sql(ORMEnum.EclipseLink, ExistsInAJoin)).Contains("join Purchase o on (o.CustomerId = c.CustomerId and exists (select r.OrderId from Purchase r where r.CustomerId = c.CustomerId and r.Rating > 3))", StringComparison.Ordinal), Text(Sql(ORMEnum.EclipseLink, ExistsInAJoin)));
    }

    /// <summary>NHibernate 5.7 accepts the subquery after with and writes it into the ON of its SQL (<c>NHibernateHqlProviderTest</c>).</summary>
    [Fact]
    public void NHibernateWritesTheSubqueryAfterWith()
    {
        Assert.True(Text(Sql(ORMEnum.NHibernate, InOverASubquery)).Contains("inner join Purchase o with o.CustomerId = c.CustomerId and c.CustomerId in (select r.CustomerId from Purchase r where r.Rating > 3)", StringComparison.Ordinal), Text(Sql(ORMEnum.NHibernate, InOverASubquery)));
        Assert.True(Text(Sql(ORMEnum.NHibernate, NotInOverACorrelatedSubqueryInALeftJoin)).Contains("left join Purchase o with o.CustomerId = c.CustomerId and not (c.CustomerId in (select r.CustomerId from Purchase r where r.CustomerId = c.CustomerId and r.Rating > 3))", StringComparison.Ordinal), Text(Sql(ORMEnum.NHibernate, NotInOverACorrelatedSubqueryInALeftJoin)));
    }

    /// <summary>
    /// LINQ: the conjunct with the subquery names the row of the chain, so the join is the
    /// correlated SelectMany (decision 113) with Contains or Any in its predicate - EF Core 10
    /// makes a CROSS APPLY or an OUTER APPLY of it with the subquery inside, the rows of the join
    /// (<c>EFCoreJoinSubqueryProviderTest</c>).
    /// </summary>
    [Fact]
    public void LinqWritesTheSubqueryInsideTheCorrelatedSelectMany()
    {
        var inner = Text(Sql(ORMEnum.EFCore, InOverASubquery));
        Assert.True(inner.Contains(".SelectMany(c => ctx.Set<Purchase>().Where(o => o.CustomerId == c.CustomerId && ctx.Set<Purchase>().Where(r => r.Rating > 3).Select(r => r.CustomerId).Contains(c.CustomerId)), (c, o) => new { c, o })", StringComparison.Ordinal), inner);

        var left = Text(Sql(ORMEnum.EFCore, NotInOverACorrelatedSubqueryInALeftJoin));
        Assert.True(left.Contains(".SelectMany(c => ctx.Set<Purchase>().Where(o => o.CustomerId == c.CustomerId && !(ctx.Set<Purchase>().Where(r => r.CustomerId == c.CustomerId && r.Rating > 3).Select(r => r.CustomerId).Contains(c.CustomerId))).DefaultIfEmpty(), (c, o) => new { c, o })", StringComparison.Ordinal), left);

        var exists = Text(Sql(ORMEnum.EFCore, ExistsInAJoin));
        Assert.True(exists.Contains(".SelectMany(c => ctx.Set<Purchase>().Where(o => o.CustomerId == c.CustomerId && ctx.Set<Purchase>().Where(r => r.CustomerId == c.CustomerId && r.Rating > 3).Any()), (c, o) => new { c, o })", StringComparison.Ordinal), exists);
    }

    // ---- every language in, T-SQL out ----------------------------------------------------

    /// <summary>LINQ names the value a Select projects after its member, and T-SQL writes the name it gives; HQL and JPQL read a bare path as the column it is.</summary>
    private const string InOverASubqueryFromAMemberLanguage =
        "SELECT c.Name AS Name, o.OrderId AS OrderId FROM Customers AS c INNER JOIN Purchases o ON o.CustomerId = c.CustomerId AND c.CustomerId IN (SELECT r.CustomerId AS CustomerId FROM Purchases AS r WHERE r.Rating > 3)";

    private const string ExistsInAJoinFromAMemberLanguage =
        "SELECT c.Name AS Name, o.OrderId AS OrderId FROM Customers AS c INNER JOIN Purchases o ON o.CustomerId = c.CustomerId AND EXISTS (SELECT * FROM Purchases AS r WHERE r.CustomerId = c.CustomerId AND r.Rating > 3)";

    [Fact]
    public void EveryLanguageReadsTheSubqueryInTheJoinCondition()
    {
        const string jpql = "select c.Name as Name, o.OrderId as OrderId from Customer c join Purchase o on o.CustomerId = c.CustomerId and c.CustomerId in (select r.CustomerId from Purchase r where r.Rating > 3)";
        const string hql = "select c.Name as Name, o.OrderId as OrderId from Customer c inner join Purchase o with o.CustomerId = c.CustomerId and c.CustomerId in (select r.CustomerId from Purchase r where r.Rating > 3)";
        const string linqIn = "ctx.Customers.SelectMany(c => ctx.Purchases.Where(o => o.CustomerId == c.CustomerId && ctx.Purchases.Where(r => r.Rating > 3).Select(r => r.CustomerId).Contains(c.CustomerId)), (c, o) => new { c, o }).Select(t => new { Name = t.c.Name, OrderId = t.o.OrderId })";
        const string linqExists = "ctx.Customers.SelectMany(c => ctx.Purchases.Where(o => o.CustomerId == c.CustomerId && ctx.Purchases.Where(r => r.CustomerId == c.CustomerId && r.Rating > 3).Any()), (c, o) => new { c, o }).Select(t => new { Name = t.c.Name, OrderId = t.o.OrderId })";

        var fromHibernate = Text(Jpql(ORMEnum.Dapper, jpql));
        Assert.True(InOverASubquery == fromHibernate, fromHibernate);
        var fromEclipseLink = Text(Jpql(ORMEnum.Dapper, jpql, eclipseLink: true));
        Assert.True(InOverASubquery == fromEclipseLink, fromEclipseLink);
        var fromNHibernate = Text(Hql(ORMEnum.Dapper, hql));
        Assert.True(InOverASubquery == fromNHibernate, fromNHibernate);
        var fromLinq = Text(Linq(ORMEnum.Dapper, linqIn));
        Assert.True(InOverASubqueryFromAMemberLanguage == fromLinq, fromLinq);
        var fromLinqExists = Text(Linq(ORMEnum.Dapper, linqExists));
        Assert.True(ExistsInAJoinFromAMemberLanguage == fromLinqExists, fromLinqExists);
    }

    /// <summary>The mechanical rules of a subquery operand hold in a join condition as in a filter: IN needs exactly one projected column.</summary>
    [Fact]
    public void TheRulesOfASubqueryOperandHoldInAJoinCondition()
    {
        var builder = Sql(ORMEnum.Dapper, "SELECT c.Name AS Name FROM Customers AS c INNER JOIN Purchases o ON o.CustomerId = c.CustomerId AND c.CustomerId IN (SELECT r.CustomerId, r.OrderId FROM Purchases AS r)");
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Subquery);
    }
}
