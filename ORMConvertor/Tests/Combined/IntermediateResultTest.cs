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
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// A query as a source of rows (decision 112): a common table expression and a derived table
/// are both read into a named intermediate result of the whole query, a row source refers to
/// it by name, its columns are its projections, typed by the template from the body. Dapper,
/// MyBatis, Hibernate and EF Core write it - WITH before the statement, HQL's with, a local
/// variable of the LINQ method -, NHibernate and EclipseLink refuse it, and the rules every
/// definition is held to refuse in one place for every target. The categories of T2 over the
/// shared files carry the shape through every direction and the fourth level
/// (<c>QueryShapeMatrixTest</c>, the differential matrix); this class names the rules.
/// </summary>
public class IntermediateResultTest
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
        Map("Order", "Orders", ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int), ("Total", ScalarType.Decimal), ("PlacedAt", ScalarType.DateTime)),
    ];

    private static AbstractQueryBuilder FromSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder FromHql(AbstractQueryBuilder builder, string hql, bool eclipseLink = false)
    {
        builder.EntityMaps = Maps();
        JakartaPersistence.JpqlQueryParser parser = eclipseLink
            ? new EclipseLinkJpqlQueryParser(() => builder)
            : new HibernateJpqlQueryParser(() => builder);
        parser.Parse(ConversionContentType.JpqlQuery, hql, Maps());
        return builder;
    }

    private static AbstractQueryBuilder FromLinq(AbstractQueryBuilder builder, string linq, bool nhibernate = false)
    {
        builder.EntityMaps = Maps();
        LinqParsing.LinqQueryParser parser = nhibernate
            ? new NHibernateLinqQueryParser(() => builder)
            : new EFCoreLinqQueryParser(() => builder);
        parser.Parse(ConversionContentType.CSharp, linq, Maps());
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return built.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature, string named)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature && r.Reason.Contains(named, StringComparison.Ordinal));
    }

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    private const string Histogram = """
        SELECT oc.Orders AS Orders, COUNT(*) AS Customers
        FROM (SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders
              FROM Orders AS o
              GROUP BY o.CustomerId) AS oc
        GROUP BY oc.Orders
        """;

    private const string ReadTwice = """
        WITH OrderCount AS (
            SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders
            FROM Orders AS o
            GROUP BY o.CustomerId)
        SELECT c.Name AS Name, oc.Orders AS Orders
        FROM Customers AS c
        JOIN OrderCount AS oc ON oc.CustomerId = c.CustomerId
        WHERE oc.Orders = (SELECT MAX(m.Orders) FROM OrderCount AS m)
        """;

    // ---- reading T-SQL, writing T-SQL ------------------------------------------------

    [Fact]
    public void ACommonTableExpressionRoundTripsAsOne()
    {
        var sql = Sql(FromSql(new DapperSqlQueryBuilder(), ReadTwice));

        Assert.Equal(
            "WITH OrderCount AS ( SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId ) "
            + "SELECT c.Name AS Name, oc.Orders AS Orders FROM Customers AS c "
            + "INNER JOIN OrderCount oc ON oc.CustomerId = c.CustomerId "
            + "WHERE oc.Orders = (SELECT MAX(m.Orders) FROM OrderCount AS m)",
            OneLine(sql));
    }

    /// <summary>The model keeps no syntax: a derived table is a definition named by its alias, and T-SQL writes every definition before the statement.</summary>
    [Fact]
    public void ADerivedTableIsWrittenAsACommonTableExpression()
    {
        var sql = Sql(FromSql(new DapperSqlQueryBuilder(), Histogram));

        Assert.Equal(
            "WITH oc AS ( SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId ) "
            + "SELECT oc.Orders AS Orders, COUNT(*) AS Customers FROM oc AS oc GROUP BY oc.Orders",
            OneLine(sql));
    }

    [Fact]
    public void ADerivedTableAfterAJoinIsJoinedByItsAlias()
    {
        const string sql = """
            SELECT c.Name AS Name, t.Spent AS Spent
            FROM Customers AS c
            LEFT JOIN (SELECT o.CustomerId AS CustomerId, SUM(o.Total) AS Spent FROM Orders AS o GROUP BY o.CustomerId) AS t
                ON t.CustomerId = c.CustomerId
            """;

        var text = OneLine(Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        Assert.StartsWith("WITH t AS ( SELECT o.CustomerId AS CustomerId, SUM(o.Total) AS Spent", text);
        Assert.Contains("LEFT JOIN t t ON t.CustomerId = c.CustomerId", text);
    }

    /// <summary>A derived table nested in a subquery is lifted to the query: it sees nothing of the scope around it.</summary>
    [Fact]
    public void ADerivedTableInsideASubqueryIsLiftedToTheQuery()
    {
        const string sql = """
            SELECT c.Name AS Name
            FROM Customers AS c
            WHERE c.CustomerId IN (SELECT big.CustomerId FROM (SELECT o.CustomerId AS CustomerId FROM Orders AS o WHERE o.Total > 100) AS big)
            """;

        var text = OneLine(Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        Assert.StartsWith("WITH big AS ( SELECT o.CustomerId AS CustomerId FROM Orders AS o WHERE o.Total > 100 )", text);
        Assert.Contains("IN (SELECT big.CustomerId FROM big AS big)", text);
    }

    /// <summary>The column list renames the columns of the body - an exact rewrite every target writes as aliases.</summary>
    [Fact]
    public void AColumnListIsReadAsTheAliasesOfTheBody()
    {
        const string sql = "WITH t(Who, Many) AS (SELECT o.CustomerId, COUNT(*) FROM Orders AS o GROUP BY o.CustomerId) SELECT t.Who, t.Many FROM t";

        var text = OneLine(Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        Assert.Contains("SELECT o.CustomerId AS Who, COUNT(*) AS Many FROM Orders AS o", text);
    }

    [Fact]
    public void AColumnListOfAnotherLengthRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), "WITH t(A, B, C) AS (SELECT o.CustomerId, COUNT(*) FROM Orders AS o GROUP BY o.CustomerId) SELECT t.A FROM t"),
            QueryFeature.IntermediateResult,
            "names 3 columns");

    /// <summary>The whole row of a definition has no class to materialize into, so Dapper reads it untyped (decision 104).</summary>
    [Fact]
    public void TheWholeRowOfADefinitionMaterializesUntyped()
    {
        var method = Artifact(
            FromSql(new DapperSqlQueryBuilder(), "WITH t AS (SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId) SELECT * FROM t"),
            ConversionContentType.CSharpQuery);

        Assert.Contains("List<dynamic>", method);
        Assert.Contains("connection.Query(", method);
    }

    /// <summary>
    /// T-SQL itself refuses an ordering without a slice in a common table expression, so the
    /// unsliced one is stated where it can be: an ordering ahead of a projection the chain then
    /// filters.
    /// </summary>
    [Fact]
    public void AnOrderingInsideADefinitionWithoutASliceIsDroppedAndOneWithASliceStays()
    {
        var unsliced = FromLinq(new DapperSqlQueryBuilder(),
            "var q = ctx.Orders.OrderBy(o => o.Total).Select(o => new { o.CustomerId }).Where(x => x.CustomerId > 1).ToList();");
        var text = OneLine(Sql(unsliced));
        Assert.DoesNotContain("ORDER BY", text);
        Assert.Contains(unsliced.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Ordering);

        var sliced = OneLine(Sql(FromSql(new DapperSqlQueryBuilder(),
            "WITH t AS (SELECT TOP (3) o.CustomerId AS CustomerId FROM Orders AS o ORDER BY o.Total DESC) SELECT t.CustomerId FROM t")));
        Assert.Contains("SELECT TOP (3) o.CustomerId AS CustomerId FROM Orders AS o ORDER BY o.Total DESC", sliced);
    }

    /// <summary>A body that is a set operation is a definition too; its columns are named by the left operand.</summary>
    [Fact]
    public void ASetOperationBodyIsCarried()
    {
        const string sql = """
            WITH Ids AS (SELECT o.CustomerId AS Id FROM Orders AS o UNION SELECT c.CustomerId FROM Customers AS c)
            SELECT i.Id FROM Ids AS i
            """;

        var text = OneLine(Sql(FromSql(new DapperSqlQueryBuilder(), sql)));

        Assert.StartsWith("WITH Ids AS ( SELECT o.CustomerId AS Id FROM Orders AS o UNION SELECT c.CustomerId FROM Customers AS c )", text);
        Assert.Contains("SELECT i.Id FROM Ids AS i", text);
    }

    // ---- what a definition may not do -------------------------------------------------

    [Fact]
    public void ARecursiveCommonTableExpressionRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(),
                "WITH t AS (SELECT c.CustomerId AS Id FROM Customers AS c UNION ALL SELECT t.Id FROM t) SELECT t.Id FROM t"),
            QueryFeature.IntermediateResult,
            "recursive WITH");

    /// <summary>A derived table correlated with the query around it is a lateral reference, which T-SQL's WITH cannot hold and HQL writes only with lateral.</summary>
    [Fact]
    public void ALateralReferenceRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), """
                SELECT c.Name AS Name FROM Customers AS c
                WHERE EXISTS (SELECT d.OrderId FROM (SELECT o.OrderId AS OrderId FROM Orders AS o WHERE o.CustomerId = c.CustomerId) AS d)
                """),
            QueryFeature.IntermediateResult,
            "lateral reference");

    [Fact]
    public void ADefinitionOfTheWholeEntityRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), "WITH t AS (SELECT * FROM Orders AS o) SELECT t.OrderId FROM t"),
            QueryFeature.IntermediateResult,
            "whole entity");

    [Fact]
    public void AnUnnamedColumnOfADefinitionRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), "WITH t AS (SELECT o.CustomerId AS CustomerId, COUNT(*) FROM Orders AS o GROUP BY o.CustomerId) SELECT t.CustomerId FROM t"),
            QueryFeature.IntermediateResult,
            "has no name");

    [Fact]
    public void TwoColumnsOfOneNameRefuse()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), "WITH t AS (SELECT o.CustomerId, c.CustomerId FROM Orders AS o JOIN Customers AS c ON c.CustomerId = o.CustomerId) SELECT t.CustomerId FROM t"),
            QueryFeature.IntermediateResult,
            "two columns named 'CustomerId'");

    /// <summary>A derived table is lifted to the query by its alias, so the alias may not be the name of a table the statement reads.</summary>
    [Fact]
    public void ADerivedTableNamedLikeATableRefuses()
        => AssertRefused(
            FromSql(new DapperSqlQueryBuilder(), "SELECT Orders.CustomerId FROM (SELECT o.CustomerId AS CustomerId FROM Orders AS o) AS Orders"),
            QueryFeature.IntermediateResult,
            "takes a name");

    // ---- the targets ------------------------------------------------------------------

    [Theory]
    [InlineData(Histogram)]
    [InlineData(ReadTwice)]
    public void NHibernateAndEclipseLinkRefuseTheShape(string sql)
    {
        AssertRefused(FromSql(new NHibernateHqlQueryBuilder(), sql), QueryFeature.IntermediateResult, "cannot express");
        AssertRefused(FromSql(new EclipseLinkJpqlQueryBuilder(), sql), QueryFeature.IntermediateResult, "cannot express");
    }

    [Fact]
    public void HibernateWritesWithAndCountsTheRowsOfADefinitionByStar()
    {
        var jpql = OneLine(Artifact(FromSql(new HibernateJpqlQueryBuilder(), Histogram), ConversionContentType.JpqlQuery));

        Assert.Equal(
            "with oc as ( select o.CustomerId as CustomerId, count(o) as Orders from Order o group by o.CustomerId ) "
            + "select oc.Orders as Orders, count(*) as Customers from oc oc group by oc.Orders",
            jpql);
    }

    /// <summary>HQL refuses `select d` over a derived row, so the whole row is written as its columns; a slice of a body is written into the text, with its parameter bound.</summary>
    [Fact]
    public void HibernateWritesTheWholeRowAsItsColumnsAndASliceIntoTheBody()
    {
        var method = Artifact(
            FromSql(new HibernateJpqlQueryBuilder(),
                "WITH t AS (SELECT TOP (@take) o.CustomerId AS CustomerId, o.Total AS Total FROM Orders AS o ORDER BY o.Total DESC) SELECT * FROM t"),
            ConversionContentType.JavaQuery);

        Assert.Contains("order by o.Total desc fetch first :take rows only", method);
        Assert.Contains("select t.CustomerId, t.Total", method);
        Assert.Contains(".setParameter(\"take\", take)", method);
        Assert.Contains("public static Query query(EntityManager em, int take)", method);
    }

    [Fact]
    public void EFCoreHoldsADefinitionInAVariableAndJoinsIt()
    {
        var method = Artifact(FromSql(new EFCoreLinqQueryBuilder(), ReadTwice), ConversionContentType.CSharpQuery);

        Assert.Contains("public static IQueryable Query(DbContext ctx)", method);
        Assert.Contains("var OrderCount = ctx.Set<Order>()", method);
        Assert.Contains(".Join(OrderCount, c => c.CustomerId, oc => oc.CustomerId, (c, oc) => new { c, oc })", method);
        Assert.Contains("OrderCount.Max(m => m.Orders)", method);
    }

    /// <summary>
    /// The scalar of a parameter compared with a column of a definition comes from the
    /// column its body projects: a COUNT is a long, a column of an entity what the mapping
    /// says (decisions 083 and 112).
    /// </summary>
    [Fact]
    public void AParameterIsTypedFromTheColumnOfADefinition()
    {
        const string sql = """
            SELECT t.CustomerId FROM (SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders, MAX(o.PlacedAt) AS LastAt FROM Orders AS o GROUP BY o.CustomerId) AS t
            WHERE t.Orders >= @minOrders AND t.LastAt > @since AND t.CustomerId <> @excluded
            """;

        var method = Artifact(FromSql(new DapperSqlQueryBuilder(), sql), ConversionContentType.CSharpQuery);

        Assert.Contains("long minOrders, DateTime since, int excluded", method);
    }

    /// <summary>
    /// A parameter compared with a column of a definition asks the catalog for the table
    /// behind the column the definition took it from (decisions 105 and 112), never for the
    /// definition, which is no table; a COUNT asks for nothing.
    /// </summary>
    [Fact]
    public void TheCatalogDemandGoesThroughADefinitionToItsTable()
    {
        var builder = new DapperSqlQueryBuilder();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, """
            WITH t AS (SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Shop.Orders AS o GROUP BY o.CustomerId)
            SELECT t.CustomerId FROM t WHERE t.CustomerId = @id AND t.Orders > @many
            """);

        var demand = builder.CatalogDemand();

        Assert.Single(demand);
        Assert.Contains(demand, d => d.Names(QueryTableDemand.Of("Shop.Orders")));
    }

    // ---- reading HQL ------------------------------------------------------------------

    [Fact]
    public void HqlWithAndASubqueryInFromAreRead()
    {
        const string hql = """
            with oc as (select o.CustomerId as CustomerId, count(o) as Orders from Order o group by o.CustomerId)
            select c.Name as Name, x.Orders as Orders
            from Customer c join oc x on x.CustomerId = c.CustomerId
            where x.Orders > (select avg(d.Orders) from (select o2.CustomerId as CustomerId, count(o2) as Orders from Order o2 group by o2.CustomerId) d)
            """;

        var text = OneLine(Sql(FromHql(new DapperSqlQueryBuilder(), hql)));

        Assert.StartsWith("WITH oc AS ( SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId ), d AS (", text);
        Assert.Contains("INNER JOIN oc x ON x.CustomerId = c.CustomerId", text);
        Assert.Contains("(SELECT AVG(d.Orders) FROM d AS d)", text);
    }

    [Fact]
    public void AMaterializationHintIsALoss()
    {
        var builder = FromHql(new DapperSqlQueryBuilder(),
            "with oc as materialized (select o.CustomerId as CustomerId, count(o) as Orders from Order o group by o.CustomerId) select x.Orders from oc x");

        Assert.Contains("WITH oc AS (", Sql(builder));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.IntermediateResult);
    }

    /// <summary>JPQL has neither form, so the EclipseLink profile reads neither.</summary>
    [Theory]
    [InlineData("with oc as (select o.CustomerId as CustomerId from Order o) select x.CustomerId from oc x", "with clause")]
    [InlineData("select x.CustomerId from (select o.CustomerId as CustomerId from Order o) x", "subquery as a source of rows")]
    public void EclipseLinkReadsNeither(string jpql, string named)
    {
        var builder = FromHql(new DapperSqlQueryBuilder(), jpql, eclipseLink: true);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains(named, StringComparison.Ordinal));
    }

    // ---- reading LINQ -----------------------------------------------------------------

    /// <summary>A filter over projected rows ranges over the result of the projection: the chain so far is a definition named by the filter's parameter.</summary>
    [Fact]
    public void AFilterAfterAProjectionReadsTheProjectionAsADefinition()
    {
        var text = OneLine(Sql(FromLinq(new DapperSqlQueryBuilder(),
            "var q = ctx.Orders.Select(o => new { Id = o.OrderId, o.Total }).Where(x => x.Total > 100).ToList();")));

        Assert.Equal(
            "WITH x AS ( SELECT o.OrderId AS Id, o.Total AS Total FROM Orders AS o ) SELECT * FROM x AS x WHERE x.Total > 100",
            text);
    }

    [Fact]
    public void AGroupingOverAGroupedProjectionReadsItAsADefinition()
    {
        var text = OneLine(Sql(FromLinq(new DapperSqlQueryBuilder(), """
            var q = ctx.Orders
                .GroupBy(o => o.CustomerId)
                .Select(g => new { CustomerId = g.Key, Orders = g.Count() })
                .GroupBy(oc => oc.Orders)
                .Select(g => new { Orders = g.Key, Customers = g.Count() })
                .ToList();
            """)));

        Assert.Equal(
            "WITH oc AS ( SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId ) "
            + "SELECT oc.Orders AS Orders, COUNT(*) AS Customers FROM oc AS oc GROUP BY oc.Orders",
            text);
    }

    /// <summary>A variable that holds a query is joined and aggregated: one definition, named by the variable, read twice.</summary>
    [Fact]
    public void AVariableJoinedAndAggregatedIsOneDefinition()
    {
        var builders = new List<AbstractQueryBuilder>();
        var linq = """
            var orderCount = ctx.Orders.GroupBy(o => o.CustomerId).Select(g => new { CustomerId = g.Key, Orders = g.Count() });
            var q = ctx.Customers
                .Join(orderCount, c => c.CustomerId, oc => oc.CustomerId, (c, oc) => new { c, oc })
                .Where(x => x.oc.Orders == orderCount.Max(m => m.Orders))
                .Select(x => new { Name = x.c.Name, Orders = x.oc.Orders })
                .ToList();
            """;

        new EFCoreLinqQueryParser(() =>
        {
            var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
            builders.Add(builder);
            return builder;
        }).Parse(ConversionContentType.CSharp, linq, Maps());

        var text = OneLine(Sql(Assert.Single(builders)));

        Assert.Equal(
            "WITH orderCount AS ( SELECT o.CustomerId AS CustomerId, COUNT(*) AS Orders FROM Orders AS o GROUP BY o.CustomerId ) "
            + "SELECT c.Name AS Name, oc.Orders AS Orders FROM Customers AS c "
            + "INNER JOIN orderCount oc ON c.CustomerId = oc.CustomerId "
            + "WHERE oc.Orders = (SELECT MAX(m.Orders) FROM orderCount AS m)",
            text);
    }

    /// <summary>A slice the next step does not commute with is the body of a definition rather than a refusal.</summary>
    [Fact]
    public void AFilterAfterASliceOfAProjectionReadsTheSliceAsADefinition()
    {
        var text = OneLine(Sql(FromLinq(new DapperSqlQueryBuilder(),
            "var q = ctx.Orders.OrderByDescending(o => o.Total).Select(o => new { o.CustomerId, o.Total }).Take(5).Where(x => x.CustomerId > 1).ToList();")));

        Assert.StartsWith("WITH x AS ( SELECT TOP (5) o.CustomerId AS CustomerId, o.Total AS Total FROM Orders AS o ORDER BY o.Total DESC )", text);
        Assert.EndsWith("FROM x AS x WHERE x.CustomerId > 1", text);
    }

    /// <summary>An ordering by a member of the projected rows is an ordering by the alias of the projection (decision 073).</summary>
    [Fact]
    public void AnOrderingAfterAProjectionNamesItsAlias()
    {
        var text = OneLine(Sql(FromLinq(new DapperSqlQueryBuilder(),
            "var q = ctx.Orders.Select(o => new { Id = o.OrderId, o.Total }).OrderByDescending(p => p.Total).ToList();")));

        Assert.Equal("SELECT o.OrderId AS Id, o.Total AS Total FROM Orders AS o ORDER BY Total DESC", text);
    }

    /// <summary>A later step that calls the rows by another parameter means the same alias; the parameter used to be written as the qualifier.</summary>
    [Fact]
    public void ALaterLambdaParameterStandsForTheSourceAlias()
    {
        var text = OneLine(Sql(FromLinq(new DapperSqlQueryBuilder(),
            "var q = ctx.Orders.Where(o => o.Total > 1).OrderBy(x => x.Total).ToList();")));

        Assert.Equal("SELECT * FROM Orders AS o WHERE o.Total > 1 ORDER BY o.Total ASC", text);
    }

    /// <summary>NHibernate's provider translates into HQL, which has no intermediate result: the composition is refused by name, not misread.</summary>
    [Fact]
    public void NHibernateLinqRefusesTheComposition()
        => AssertRefused(
            FromLinq(new DapperSqlQueryBuilder(),
                "var q = session.Query<Order>().GroupBy(o => o.CustomerId).Select(g => new { CustomerId = g.Key, Orders = g.Count() }).Where(x => x.Orders > 1).ToList();",
                nhibernate: true),
            QueryFeature.IntermediateResult,
            "does not compose");

    // ---- EF Core ordering around the projection -----------------------------------------

    /// <summary>
    /// An ordering by a projection alias next to one by a column: EF Core keeps only the
    /// ordering after the projection, so both keys go there, the column under the member that
    /// projects it - with a slice, a key left before the projection would select other rows.
    /// </summary>
    [Fact]
    public void EFCoreOrdersByEveryKeyAfterTheProjectionWhenOneNamesIt()
    {
        var method = Artifact(
            FromSql(new EFCoreLinqQueryBuilder(), """
                SELECT TOP (10) o.CustomerId AS CustomerId, COUNT(*) AS Orders
                FROM Orders AS o
                GROUP BY o.CustomerId
                ORDER BY Orders DESC, o.CustomerId ASC
                """),
            ConversionContentType.CSharpQuery);

        Assert.Contains(".OrderByDescending(p => p.Orders)", method);
        Assert.Contains(".ThenBy(p => p.CustomerId)", method);
        Assert.DoesNotContain(".OrderBy(g =>", method);
    }

    [Fact]
    public void EFCoreRefusesASlicedOrderingWhoseKeyIsNotProjected()
        => AssertRefused(
            FromSql(new EFCoreLinqQueryBuilder(), """
                SELECT TOP (10) o.CustomerId AS CustomerId, COUNT(*) AS Orders
                FROM Orders AS o
                GROUP BY o.CustomerId, o.Total
                ORDER BY Orders DESC, o.Total ASC
                """),
            QueryFeature.Ordering,
            "discards the one before it");
}
