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
using Model.QueryInstructions.Enums;
using MyBatisWrappers;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The cross join, end to end (decision 196): comma-separated sources in T-SQL, HQL and
/// JPQL, <c>CROSS JOIN</c>, HQL's <c>cross join</c> and a LINQ <c>SelectMany</c> over a second
/// query root are read as a join without a condition, whatever the filter says about the two
/// rows, and every target writes it in its own spelling - the condition stays in the filter.
/// The spellings rest on measured facts: <c>EFCore/EFCoreCrossJoinProviderTest</c>,
/// <c>NHibernate/NHibernateHqlProviderTest</c>, <c>NHibernate/NHibernateLinqProviderTest</c>
/// and the Java <c>CrossJoinClaimsTest</c> of both JPA implementations. The tool thereby reads
/// every artifact it writes: the EclipseLink artifact of a subquery with a join, which the
/// JPQL parser used to refuse, reads back.
/// </summary>
public class CrossJoinTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var properties = columns.Select(c => new Property { Name = c.Name, Type = LangType.Scalar(c.Type) }).ToList();

        return new EntityMap
        {
            Entity = new Entity { Name = entity, Properties = properties },
            Table = table,
            Schema = "Sales",
            PropertyMaps = properties.Select(p => new PropertyMap { Property = p, ColumnName = p.Name }).ToList(),
        };
    }

    private static EntityMap[] Maps() =>
    [
        Map("Customer", "Customers", ("CustomerId", ScalarType.Int), ("Name", ScalarType.String)),
        Map("Purchase", "Orders", ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int), ("Total", ScalarType.Decimal)),
        Map("Product", "Products", ("ProductId", ScalarType.Int), ("ProductName", ScalarType.String)),
    ];

    private static AbstractQueryBuilder ParseSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder ParseHql(AbstractQueryBuilder builder, string hql)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, maps);
        return builder;
    }

    private static AbstractQueryBuilder ParseJpql(AbstractQueryBuilder builder, string jpql, bool eclipseLink = false)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        IQueryParser parser = eclipseLink
            ? new EclipseLinkJpqlQueryParser(() => builder)
            : new HibernateJpqlQueryParser(() => builder);
        parser.Parse(ConversionContentType.JpqlQuery, jpql, maps);
        return builder;
    }

    private static AbstractQueryBuilder ParseLinq(AbstractQueryBuilder builder, string chain, bool nhibernate = false)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        IQueryParser parser = nhibernate
            ? new NHibernateLinqQueryParser(() => builder)
            : new EFCoreLinqQueryParser(() => builder);
        parser.Parse(ConversionContentType.CSharpQuery, $$"""
            public void Query()
            {
                var q = ({{chain}}).ToList();
            }
            """, maps);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var outputs = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        return outputs.SingleOrDefault(s => s.ContentType == type)?.Content
               ?? throw new Xunit.Sdk.XunitException(
                   $"No {type} artifact among {string.Join(", ", outputs.Select(o => o.ContentType))}; records:\n"
                   + string.Join("\n", builder.Records.Select(r => $"{r.Kind} {r.Feature}: {r.Reason}")));
    }

    private static string Sql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.SqlQuery);

    private static string Hql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.HqlQuery);

    private static string Jpql(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.JpqlQuery);

    private static string CSharp(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpQuery);

    private static string Linq(AbstractQueryBuilder builder) => Artifact(builder, ConversionContentType.CSharpLinqQuery);

    private const string CommaSql = "SELECT c.Name, o.OrderId FROM Sales.Customers AS c, Sales.Orders AS o WHERE o.CustomerId = c.CustomerId";

    private const string CrossJoinSql = "SELECT c.Name, o.OrderId FROM Sales.Customers AS c CROSS JOIN Sales.Orders AS o WHERE o.CustomerId = c.CustomerId";

    private const string CommaHql = "select c.Name, o.OrderId from Customer c, Purchase o where o.CustomerId = c.CustomerId";

    private const string CrossJoinHql = "select c.Name, o.OrderId from Customer c cross join Purchase o where o.CustomerId = c.CustomerId";

    private const string SecondFrom = "from c in ctx.Customers from o in ctx.Set<Purchase>() where o.CustomerId == c.CustomerId select new { c.Name, o.OrderId }";

    // ---- read from every source language ------------------------------------------------

    /// <summary>Both T-SQL spellings are the same join without a condition, and the filter stays as written.</summary>
    [Theory]
    [InlineData(CommaSql)]
    [InlineData(CrossJoinSql)]
    public void TSqlReadsTheCrossJoin(string sql)
    {
        var written = Sql(ParseSql(new DapperSqlQueryBuilder(), sql));

        Assert.Contains("FROM Sales.Customers AS c", written);
        Assert.Contains("CROSS JOIN Sales.Orders o", written);
        Assert.Contains("WHERE o.CustomerId = c.CustomerId", written);
        Assert.DoesNotContain(" ON ", written);
    }

    /// <summary>HQL's comma and its cross join read alike, and the HQL target writes the keyword, never the comma (NHibernateHqlProviderTest).</summary>
    [Theory]
    [InlineData(CommaHql)]
    [InlineData(CrossJoinHql)]
    public void HqlReadsTheCrossJoin(string hql)
    {
        var written = Hql(ParseHql(new NHibernateHqlQueryBuilder(), hql));

        Assert.Contains("from Customer c", written);
        Assert.Contains("cross join Purchase o", written);
        Assert.Contains("where o.CustomerId = c.CustomerId", written);
        Assert.DoesNotContain(" with ", written);
    }

    /// <summary>The JPQL parser of both profiles reads the comma - the shape the EclipseLink builder writes inside a subquery - and HQL's keyword as well.</summary>
    [Theory]
    [InlineData(CommaHql, false)]
    [InlineData(CommaHql, true)]
    [InlineData(CrossJoinHql, false)]
    public void JpqlReadsTheCrossJoin(string jpql, bool eclipseLink)
    {
        var sql = Sql(ParseJpql(new DapperSqlQueryBuilder(), jpql, eclipseLink));

        Assert.Contains("CROSS JOIN Sales.Orders o", sql);
        Assert.Contains("WHERE o.CustomerId = c.CustomerId", sql);
    }

    /// <summary>A second <c>from</c> over a second root and the SelectMany it rewrites to are the cross join; the where clause after it is the filter.</summary>
    [Theory]
    [InlineData(SecondFrom, false)]
    [InlineData("ctx.Customers.SelectMany(c => ctx.Set<Purchase>(), (c, o) => new { c, o }).Where(x => x.o.CustomerId == x.c.CustomerId).Select(x => new { x.c.Name, x.o.OrderId })", false)]
    [InlineData("session.Query<Customer>().SelectMany(c => session.Query<Purchase>(), (c, o) => new { c, o }).Where(x => x.o.CustomerId == x.c.CustomerId).Select(x => new { x.c.Name, x.o.OrderId })", true)]
    public void LinqReadsTheCrossJoin(string chain, bool nhibernate)
    {
        var sql = Sql(ParseLinq(new DapperSqlQueryBuilder(), chain, nhibernate));

        Assert.Contains("CROSS JOIN Sales.Orders o", sql);
        Assert.Contains("WHERE o.CustomerId = c.CustomerId", sql);
    }

    // ---- written by every target ----------------------------------------------------------

    [Fact]
    public void EveryTargetWritesTheCrossJoinInItsOwnSpelling()
    {
        Assert.Contains("CROSS JOIN Sales.Orders o", Sql(ParseSql(new DapperSqlQueryBuilder(), CommaSql)));
        Assert.Contains("CROSS JOIN Sales.Orders o", Artifact(ParseSql(new MyBatisSqlQueryBuilder(), CommaSql), ConversionContentType.XML));

        var hql = Hql(ParseSql(new NHibernateHqlQueryBuilder(), CommaSql));
        Assert.Contains("cross join Purchase o", hql);
        Assert.Contains("where o.CustomerId = c.CustomerId", hql);

        var linqBeside = Linq(ParseSql(new NHibernateHqlQueryBuilder(), CommaSql));
        Assert.Contains(".SelectMany(c => session.Query<Purchase>(), (c, o) => new { c, o })", linqBeside);
        Assert.Contains(".Where(", linqBeside);
        Assert.Contains(".o.CustomerId == ", linqBeside);

        var linq = CSharp(ParseSql(new EFCoreLinqQueryBuilder(), CommaSql));
        Assert.Contains(".SelectMany(c => ctx.Set<Purchase>(), (c, o) => new { c, o })", linq);
        Assert.Contains(".o.CustomerId == ", linq);
        Assert.DoesNotContain(".Join(", linq);

        var hibernate = Jpql(ParseSql(new HibernateJpqlQueryBuilder(), CommaSql));
        Assert.Contains("from Customer c, Purchase o", hibernate);
        Assert.Contains("where o.CustomerId = c.CustomerId", hibernate);
        Assert.DoesNotContain(" on ", hibernate);

        var eclipseLink = Jpql(ParseSql(new EclipseLinkJpqlQueryBuilder(), CommaSql));
        Assert.Contains("from Customer c, Purchase o", eclipseLink);
        Assert.Contains("where o.CustomerId = c.CustomerId", eclipseLink);
    }

    /// <summary>A cross join without any condition at all: the cartesian product, carried as it stands.</summary>
    [Fact]
    public void ACrossJoinWithoutAFilterIsCarried()
    {
        const string sql = "SELECT c.Name, p.ProductName FROM Sales.Customers AS c, Sales.Products AS p";

        Assert.Contains("CROSS JOIN Sales.Products p", Sql(ParseSql(new DapperSqlQueryBuilder(), sql)));
        Assert.Contains("cross join Product p", Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql)));
        Assert.Contains("from Customer c, Product p", Jpql(ParseSql(new HibernateJpqlQueryBuilder(), sql)));
        Assert.Contains(".SelectMany(c => ctx.Set<Product>(), (c, p) => new { c, p })", CSharp(ParseSql(new EFCoreLinqQueryBuilder(), sql)));
    }

    // ---- the order of a cross join and an entity join ---------------------------------------

    private const string JoinThenCross =
        "SELECT c.Name, o.OrderId, p.ProductName FROM Sales.Customers AS c INNER JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId CROSS JOIN Sales.Products AS p";

    private const string CrossThenJoinNamingTheRoot =
        "SELECT c.Name, o.OrderId, p.ProductName FROM Sales.Customers AS c CROSS JOIN Sales.Products AS p INNER JOIN Sales.Orders AS o ON o.CustomerId = c.CustomerId";

    private const string CrossThenJoinNamingTheCrossed =
        "SELECT c.Name, o.OrderId, p.ProductName FROM Sales.Customers AS c CROSS JOIN Sales.Products AS p INNER JOIN Sales.Orders AS o ON o.CustomerId = p.ProductId";

    /// <summary>The joins keep the order the query wrote them in every target that has one chain.</summary>
    [Theory]
    [InlineData(JoinThenCross, "INNER JOIN Sales.Orders o ON o.CustomerId = c.CustomerId CROSS JOIN Sales.Products p", "inner join Purchase o with o.CustomerId = c.CustomerId", "cross join Product p")]
    [InlineData(CrossThenJoinNamingTheRoot, "CROSS JOIN Sales.Products p INNER JOIN Sales.Orders o ON o.CustomerId = c.CustomerId", "cross join Product p", "inner join Purchase o with o.CustomerId = c.CustomerId")]
    public void TheOrderOfTheJoinsIsKept(string sql, string sqlMark, string hqlFirst, string hqlSecond)
    {
        var written = Sql(ParseSql(new DapperSqlQueryBuilder(), sql)).Replace("\r\n", "\n").Replace("\n", " ");
        Assert.Contains(sqlMark, written);

        var hql = Hql(ParseSql(new NHibernateHqlQueryBuilder(), sql));
        Assert.True(hql.IndexOf(hqlFirst, StringComparison.Ordinal) < hql.IndexOf(hqlSecond, StringComparison.Ordinal), hql);
    }

    /// <summary>
    /// JPQL spells the cross join as a further range variable in the order written: in the
    /// from clause where no entity join precedes it, after the last entity join otherwise.
    /// </summary>
    [Fact]
    public void JpqlWritesTheRangeVariableWhereTheQueryWroteTheJoin()
    {
        var joinThenCross = Jpql(ParseSql(new HibernateJpqlQueryBuilder(), JoinThenCross)).Replace("\r\n", "\n").Replace("\n", " ");
        Assert.Contains("from Customer c", joinThenCross);
        Assert.Contains("join Purchase o on o.CustomerId = c.CustomerId, Product p", joinThenCross);

        var crossThenJoin = Jpql(ParseSql(new EclipseLinkJpqlQueryBuilder(), CrossThenJoinNamingTheRoot)).Replace("\r\n", "\n").Replace("\n", " ");
        Assert.Contains("from Customer c, Product p", crossThenJoin);
        Assert.Contains("join Purchase o on o.CustomerId = c.CustomerId", crossThenJoin);
    }

    /// <summary>
    /// Hibernate 7.4.5 binds an entity join written after a comma to that range variable and
    /// refuses a condition naming another root (profile JoinBindsToTheLastRangeVariable,
    /// hibernate/CrossJoinClaimsTest), so the query goes out in native SQL for Hibernate alone
    /// (decision 113); a condition naming the range variable itself stays JPQL, and EclipseLink
    /// reads both.
    /// </summary>
    [Fact]
    public void HibernateSendsAJoinNamingARootTheCommaLeftBehindToNativeSql()
    {
        var hibernate = ParseSql(new HibernateJpqlQueryBuilder(), CrossThenJoinNamingTheRoot);
        var artifacts = hibernate.Build();
        Assert.Contains(hibernate.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Join);
        Assert.Contains(artifacts, a => a.ContentType == ConversionContentType.SqlQuery);
        Assert.DoesNotContain(artifacts, a => a.ContentType == ConversionContentType.JpqlQuery);

        var namingTheCrossed = Jpql(ParseSql(new HibernateJpqlQueryBuilder(), CrossThenJoinNamingTheCrossed)).Replace("\r\n", "\n").Replace("\n", " ");
        Assert.Contains("from Customer c, Product p", namingTheCrossed);
        Assert.Contains("join Purchase o on o.CustomerId = p.ProductId", namingTheCrossed);

        Jpql(ParseSql(new EclipseLinkJpqlQueryBuilder(), CrossThenJoinNamingTheRoot));
    }

    // ---- the artifact of the tool reads back ------------------------------------------------

    /// <summary>
    /// The case the whole-input run of 2026-10-30 found: EclipseLink writes an inner join
    /// inside a subquery as comma-separated range variables with the condition in where
    /// (decision 113, EclipseLinkQueryTest), and the JPQL parser refused that text. It reads
    /// now as a cross join under the filter, so EclipseLink's own artifact goes into every
    /// target, the subquery's rows unchanged.
    /// </summary>
    [Fact]
    public void TheEclipseLinkArtifactOfASubqueryWithAJoinReadsBack()
    {
        var first = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EclipseLink,
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
            new()
            {
                Content = $"""
                    SELECT * FROM {QueryShapeInputs.Schema}.ShopOrderLines AS ol
                    WHERE ol.ProductId IN (SELECT ol2.ProductId FROM {QueryShapeInputs.Schema}.ShopOrderLines AS ol2
                                           INNER JOIN {QueryShapeInputs.Schema}.ShopOrders AS o ON o.CompanyId = ol2.CompanyId AND o.OrderId = ol2.OrderId
                                           WHERE o.CustomerId > 1 OR o.OrderId > 2)
                    """,
                ContentType = ConversionContentType.SqlQuery,
            },
        ]);

        var jpql = first.Sources.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Contains("from ShopOrderLine ol2, ShopOrder o where", jpql);

        foreach (var target in Enum.GetValues<ORMEnum>())
        {
            var readBack = ConversionHandler.Convert(ORMEnum.EclipseLink, target,
            [
                .. QueryShapeInputs.MappingUnits(ORMEnum.EclipseLink),
                new() { Content = jpql, ContentType = ConversionContentType.JpqlQuery },
            ]);

            Assert.False(
                readBack.Records.Any(r => r.Kind == ConversionRecordKind.Failure && r.Artifact?.IsQuery() == true),
                $"EclipseLink -> {target}: " + string.Join("\n", readBack.Records.Where(r => r.Kind == ConversionRecordKind.Failure).Select(r => r.Reason)));
            Assert.Contains(readBack.Sources, s => s.ContentType.IsQuery());
        }

        var dapper = ConversionHandler.Convert(ORMEnum.EclipseLink, ORMEnum.Dapper,
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.EclipseLink),
            new() { Content = jpql, ContentType = ConversionContentType.JpqlQuery },
        ]);
        var sql = dapper.Sources.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
        Assert.Contains($"CROSS JOIN {QueryShapeInputs.Schema}.ShopOrders o", sql);
        Assert.Contains("o.CompanyId = ol2.CompanyId", sql);
        Assert.Contains("o.CustomerId > 1 OR o.OrderId > 2", sql);
    }

    // ---- what stays refused -----------------------------------------------------------------

    /// <summary>A join of any other kind without a condition would multiply rows as a cross join does; the gate refuses it before any target writes (decision 196).</summary>
    [Fact]
    public void AJoinOfAnotherKindWithoutAConditionIsRefused()
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        builder.From("Sales.Customers", "c");
        builder.Join(JoinKind.Inner, "c", "Sales.Orders", null, "o");
        builder.Project("c", "Name");

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("has no condition"));
    }

    /// <summary>The lateral joins of T-SQL stay outside the representation, refused by name (decision 070).</summary>
    [Theory]
    [InlineData("SELECT c.Name FROM Sales.Customers AS c CROSS APPLY (SELECT TOP 1 o.OrderId FROM Sales.Orders AS o WHERE o.CustomerId = c.CustomerId) AS last", "CROSS APPLY")]
    [InlineData("SELECT c.Name FROM Sales.Customers AS c OUTER APPLY (SELECT TOP 1 o.OrderId FROM Sales.Orders AS o WHERE o.CustomerId = c.CustomerId) AS last", "OUTER APPLY")]
    public void ALateralJoinIsRefusedByName(string sql, string spelled)
    {
        var builder = ParseSql(new DapperSqlQueryBuilder(), sql);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains(spelled));
    }
}
