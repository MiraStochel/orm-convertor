using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;

namespace Tests.EclipseLink;

/// <summary>
/// The query branch of decision 080 through the real orchestration. JPQL is the whole
/// language on both sides here, so most of what is asserted is that the fourth hook really
/// is empty: what EclipseLink writes is the standard form the shared builder emits, what
/// it reads is JPQL and nothing else - the limit clause HQL adds is refused here, which is
/// the difference between an empty hook and a shared parser that would accept anything.
/// </summary>
public class EclipseLinkQueryTest
{
    private static ConversionResult Convert(ORMEnum source, string query, ConversionContentType language)
        => ConversionHandler.Convert(source, ORMEnum.EclipseLink,
        [
            .. CrossFrameworkInputs.MappingUnits(source),
            new() { Content = query, ContentType = language },
        ]);

    private static string Jpql(ConversionResult result)
    {
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);
        return result.Sources.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
    }

    private static string Method(ConversionResult result)
        => result.Sources.Single(s => s.ContentType == ConversionContentType.JavaQuery).Content;

    [Fact]
    public void AWholeEntityQueryFromLinqIsATypedQuery()
    {
        var result = Convert(ORMEnum.EFCore, """
            public void Query()
            {
                var q = ctx.Customers.Where(c => c.CreditLimit > 2000).OrderBy(c => c.CustomerName).ToList();
            }
            """, ConversionContentType.CSharp);

        Assert.Equal(
            "select c\nfrom Customer c\nwhere c.CreditLimit > 2000\norder by c.CustomerName asc",
            Jpql(result).Replace("\r\n", "\n"));

        var method = Method(result);
        Assert.Contains("public static TypedQuery<Customer> query(EntityManager em) {", method);
        Assert.Contains("em.createQuery(\"\"\"", method);
        Assert.DoesNotContain("createSelectionQuery", method);
    }

    /// <summary>
    /// The window is on the query object for both implementations, because that is where
    /// the specification puts it - and EQL, unlike HQL, has no clause for it in the text.
    /// </summary>
    [Fact]
    public void PaginationLandsOnTheQueryObjectAndNotInTheText()
    {
        var result = Convert(ORMEnum.EFCore, """
            public void Query()
            {
                var q = ctx.Customers.OrderBy(c => c.CustomerName).Skip(10).Take(5).ToList();
            }
            """, ConversionContentType.CSharp);

        var jpql = Jpql(result);
        Assert.DoesNotContain("limit", jpql);
        Assert.DoesNotContain("offset", jpql);

        var method = Method(result);
        Assert.Contains("setFirstResult(10)", method);
        Assert.Contains("setMaxResults(5)", method);
    }

    /// <summary>
    /// The round trip that pins the grammar to the language (decision 062 applied to JPQL):
    /// what the builder writes, the wrapper's own parser reads back into the same query.
    /// </summary>
    [Fact]
    public void AQueryWrittenForEclipseLinkIsReadBackAsItself()
    {
        var first = Jpql(Convert(ORMEnum.EFCore, """
            public void Query()
            {
                var q = ctx.Customers.Where(c => c.CreditLimit > 2000).OrderBy(c => c.CustomerName).ToList();
            }
            """, ConversionContentType.CSharp));

        var second = Jpql(ConversionHandler.Convert(ORMEnum.EclipseLink, ORMEnum.EclipseLink,
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.EclipseLink),
            new() { Content = first, ContentType = ConversionContentType.JpqlQuery },
        ]));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The limit clause of HQL 6 is Hibernate's own and the fourth hook is what reads it.
    /// EclipseLink overrides nothing, so the same text is a syntax error here - a Failure
    /// with its place, never a query quietly read without its window (decision 070).
    /// </summary>
    [Fact]
    public void TheLimitClauseOfHqlIsNotEqlAndIsRefused()
    {
        var result = ConversionHandler.Convert(ORMEnum.EclipseLink, ORMEnum.EFCore,
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.EclipseLink),
            new()
            {
                Content = "select c\nfrom Customer c\norder by c.CustomerName asc\nlimit 5",
                ContentType = ConversionContentType.JpqlQuery,
            },
        ]);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);
        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
    }

    /// <summary>
    /// A projection is an untyped query for both implementations - the entity class cannot
    /// be handed to createQuery when the rows are not that entity (decision 077).
    /// </summary>
    [Fact]
    public void AProjectionIsAnUntypedQuery()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName, COUNT(*) AS n
            FROM Sales.Customers AS c
            GROUP BY c.CustomerName
            """, ConversionContentType.SqlQuery);

        Assert.Contains("select c.CustomerName, count(c) as n", Jpql(result));
        Assert.Contains("public static Query query(EntityManager em) {", Method(result));
    }

    /// <summary>
    /// EclipseLink 5.0.0 writes an entity join inside a subquery into no SQL at all (profile
    /// DropsJoinsInSubqueries, measured by the LDBC judge), so an inner join of a subquery
    /// goes out in JPQL's original spelling: a further range variable of the subquery's from
    /// clause, its condition the first conjunct of the where clause, a disjunction of the
    /// filter parenthesized beside it. The query stays JPQL, with no record of a fallback.
    /// </summary>
    [Fact]
    public void AnInnerJoinInsideASubqueryIsARangeVariableWithItsConditionInWhere()
    {
        var result = ConvertShop($"""
            SELECT * FROM {QueryShapeInputs.Schema}.ShopOrderLines AS ol
            WHERE ol.ProductId IN (SELECT ol2.ProductId FROM {QueryShapeInputs.Schema}.ShopOrderLines AS ol2
                                   INNER JOIN {QueryShapeInputs.Schema}.ShopOrders AS o ON o.CompanyId = ol2.CompanyId AND o.OrderId = ol2.OrderId
                                   WHERE o.CustomerId > 1 OR o.OrderId > 2)
            """);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);

        var jpql = Jpql(result);
        Assert.Contains(
            "where ol.ProductId in (select ol2.ProductId from ShopOrderLine ol2, ShopOrder o where (o.CompanyId = ol2.CompanyId and o.OrderId = ol2.OrderId) and (o.CustomerId > 1 or o.OrderId > 2))",
            jpql);
        Assert.DoesNotContain(" on ", jpql);
    }

    /// <summary>
    /// An outer join has no spelling as a range variable, so a subquery with one still sends
    /// the query to native SQL with the record that says so (decision 113).
    /// </summary>
    [Fact]
    public void AnOuterJoinInsideASubquerySendsTheQueryToNativeSql()
    {
        var result = ConvertShop($"""
            SELECT * FROM {QueryShapeInputs.Schema}.ShopOrderLines AS ol
            WHERE EXISTS (SELECT a.AllocationId FROM {QueryShapeInputs.Schema}.ShopOrderLineAllocations AS a
                          LEFT JOIN {QueryShapeInputs.Schema}.ShopOrders AS o ON o.CompanyId = a.CompanyId AND o.OrderId = a.OrderId
                          WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber AND o.CustomerId IS NULL)
            """);

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Subquery && r.Reason.Contains("outer join", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.JpqlQuery);
        Assert.Contains("em.createNativeQuery(", Method(result));
    }

    private static readonly string AggregateOverAJoinedEntityInHaving = $"""
        SELECT o.CustomerId AS CustomerId, SUM(ol.Quantity) AS Total
        FROM {QueryShapeInputs.Schema}.ShopOrders AS o
        INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
        GROUP BY o.CustomerId
        HAVING SUM(ol.Quantity) > 10
        """;

    /// <summary>
    /// EclipseLink 5.0.0 rebuilds the having clause on the root of the block when its first
    /// path is over another variable (profile RebuildsHavingOnTheRoot, measured over the FIBEN
    /// catalog and pinned by the Java suite): <c>sum(ol.Quantity)</c> over
    /// <c>from ShopOrder o join ShopOrderLine ol</c> would name the order's attribute, refused
    /// where it has none and another column where it has one. The query goes out in native SQL
    /// with the record that says so (decision 113).
    /// </summary>
    [Fact]
    public void AnAggregateOverAJoinedEntityInHavingSendsTheQueryToNativeSql()
    {
        var result = ConvertShop(AggregateOverAJoinedEntityInHaving);

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.PostAggregationFiltering && r.Reason.Contains("'ol'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.JpqlQuery);
        Assert.Contains("em.createNativeQuery(", Method(result));
        Assert.Contains("HAVING SUM(ol.Quantity) > 10", result.Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content);
    }

    /// <summary>
    /// The same inside a subquery, where the join stands as a further range variable and the
    /// subquery's root is the one the path would be resolved against.
    /// </summary>
    [Fact]
    public void AnAggregateOverAJoinedEntityInTheHavingOfASubquerySendsTheQueryToNativeSql()
    {
        var result = ConvertShop($"""
            SELECT * FROM {QueryShapeInputs.Schema}.ShopCustomers AS c
            WHERE c.CustomerId IN (SELECT o.CustomerId FROM {QueryShapeInputs.Schema}.ShopOrders AS o
                                   INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
                                   GROUP BY o.CustomerId
                                   HAVING SUM(ol.Quantity) > 10)
            """);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.PostAggregationFiltering);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.JpqlQuery);
    }

    /// <summary>
    /// An aggregate over the root of the block beside a join is what EclipseLink reads as
    /// written, and so is a path over the joined entity in the select list and the grouping:
    /// the query stays JPQL.
    /// </summary>
    [Fact]
    public void AnAggregateOverTheRootInHavingStaysJpqlBesideAJoin()
    {
        var result = ConvertShop($"""
            SELECT ol.ProductId AS ProductId, COUNT(o.OrderId) AS Orders
            FROM {QueryShapeInputs.Schema}.ShopOrders AS o
            INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            GROUP BY ol.ProductId
            HAVING COUNT(o.OrderId) > 1
            """);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        var jpql = Jpql(result);
        Assert.Contains("group by ol.ProductId", jpql);
        Assert.Contains("having count(o.OrderId) > 1", jpql);
    }

    /// <summary>
    /// The first operand that carries a variable decides, and a constant carries none: with the
    /// constant on the left the path on the right is the first.
    /// </summary>
    [Fact]
    public void AConstantBeforeTheJoinedPathStillSendsTheQueryToNativeSql()
    {
        var result = ConvertShop($"""
            SELECT o.CustomerId AS CustomerId, COUNT(o.OrderId) AS Orders
            FROM {QueryShapeInputs.Schema}.ShopOrders AS o
            INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            GROUP BY o.CustomerId
            HAVING 10 < SUM(ol.Quantity)
            """);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.PostAggregationFiltering);
    }

    /// <summary>
    /// A clause whose first path is over the root EclipseLink takes as written, the paths over
    /// the joined entity after it included, so the query stays JPQL.
    /// </summary>
    [Fact]
    public void AHavingThatOpensWithTheRootStaysJpql()
    {
        var result = ConvertShop($"""
            SELECT o.CustomerId AS CustomerId, SUM(ol.Quantity) AS Total
            FROM {QueryShapeInputs.Schema}.ShopOrders AS o
            INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            GROUP BY o.CustomerId
            HAVING COUNT(o.OrderId) > 0 AND SUM(ol.Quantity) > 10
            """);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Contains("having count(o.OrderId) > 0 and sum(ol.Quantity) > 10", Jpql(result));
    }

    /// <summary>
    /// So is a clause that opens with a CASE - the counts of LDBC IC 3 and BI 16, which the
    /// validation set judged in EclipseLink -, whatever paths the CASE and the rest name.
    /// </summary>
    [Fact]
    public void AHavingThatOpensWithACaseStaysJpql()
    {
        var result = ConvertShop($"""
            SELECT o.CustomerId AS CustomerId, COUNT(DISTINCT CASE WHEN ol.Quantity > 4 THEN ol.LineNumber END) AS Large
            FROM {QueryShapeInputs.Schema}.ShopOrders AS o
            INNER JOIN {QueryShapeInputs.Schema}.ShopOrderLines AS ol ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            GROUP BY o.CustomerId
            HAVING COUNT(DISTINCT CASE WHEN ol.Quantity > 4 THEN ol.LineNumber END) > 1 AND MAX(ol.Quantity) > 10
            """);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Contains("having count(distinct case when ol.Quantity > 4 then ol.LineNumber", Jpql(result));
    }

    /// <summary>Hibernate reads the path as it stands, so the same query stays JPQL there.</summary>
    [Fact]
    public void HibernateKeepsAnAggregateOverAJoinedEntityInHavingInJpql()
    {
        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.Hibernate,
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
            new() { Content = AggregateOverAJoinedEntityInHaving, ContentType = ConversionContentType.SqlQuery },
        ]);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Contains("having sum(ol.Quantity) > 10", Jpql(result));
    }

    /// <summary>
    /// Jakarta Persistence has the inner and the left join only, and of the two extensions
    /// EclipseLink 5.0.0 reads the right join and refuses "full join" with a syntax error
    /// (measured by the Java suite's FullJoinClaimsTest), where HQL has it; an inner join in
    /// its place would return fewer rows (decision 065). So the query goes out in native SQL,
    /// which has the join, with the record that says so (decisions 113 and 180).
    /// </summary>
    [Fact]
    public void AFullOuterJoinSendsTheQueryToNativeSql()
    {
        var result = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            FULL JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId
            """, ConversionContentType.SqlQuery);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Equal(QueryFeature.JoinKind, record.Feature);
        Assert.Contains("no full outer join", record.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.JpqlQuery);

        var method = Method(result);
        Assert.Contains("em.createNativeQuery(", method);
        Assert.Contains("FULL JOIN", method);

        var leftJoin = Convert(ORMEnum.Dapper, """
            SELECT c.CustomerName FROM Sales.Customers AS c
            LEFT JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId
            """, ConversionContentType.SqlQuery);

        Assert.Contains("left join Order o on o.CustomerId = c.CustomerId", Jpql(leftJoin));
        Assert.DoesNotContain(leftJoin.Records, r => r.Kind == ConversionRecordKind.Fallback);
    }

    /// <summary>A Dapper query over the shop domain of the matrices, into EclipseLink.</summary>
    private static ConversionResult ConvertShop(string sql)
        => ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EclipseLink,
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ]);
}
