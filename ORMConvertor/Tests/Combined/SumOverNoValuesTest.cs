using System.Collections;
using System.Globalization;
using System.Reflection;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;
using Tests.Database;
using Tests.Differential;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// SQL's SUM is NULL over no values - no rows, or rows whose values are all NULL -, and the Sum
/// of EF Core is COALESCE(SUM(x), 0) there (decision 155, the provider's facts in
/// <c>EFCore/EFCoreSumProviderTest</c>). A comparison with such a sum selected other rows: the
/// FIBEN catalog's HAVING SUM(a) &gt; (SELECT SUM(b) … correlated) kept the groups without inner
/// rows. The EF Core target writes a SUM in a scalar subquery as the projection of one group,
/// absent over no rows, taken by Max() (decision 159), and a SUM over a value that may be NULL guarded by the count of the
/// values; COALESCE(SUM(x), 0) is its plain Sum. The LINQ reader reads both shapes back as the
/// SUM. NHibernate's provider writes the bare sum, so its second form keeps the plain Sum. The
/// fourth level runs the shapes over the fixture of the differential matrix, where product 5
/// has no outgoing link and product 2 has no weight.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class SumOverNoValuesTest(TestSchemaFixture fixture)
{
    // ---- the shapes, without a database ---------------------------------------------------

    private static EntityMap Sales()
    {
        var id = new Property { Name = "SaleId", Type = LangType.Scalar(ScalarType.Int) };
        var customer = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var weight = new Property { Name = "Weight", Type = LangType.Scalar(ScalarType.Double, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Sale", Properties = [id, customer, total, weight] },
            Table = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "SaleId" },
                new PropertyMap { Property = customer, ColumnName = "CustomerId" },
                new PropertyMap { Property = total, ColumnName = "Total" },
                new PropertyMap { Property = weight, ColumnName = "Weight" },
            ],
        };
    }

    private static AbstractQueryBuilder Sql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = [Sales()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [Sales()]);
        return builder;
    }

    private static AbstractQueryBuilder Linq(string chain)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = [Sales()] };
        var source = $$"""
            public void Query()
            {
                var q = ctx.Sales.{{chain}}.ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, [Sales()]);
        return builder;
    }

    private static string Text(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback or ConversionRecordKind.Loss);

        var query = built.FirstOrDefault(s => s.ContentType == ConversionContentType.SqlQuery) ?? built.First();
        return string.Join(" ", query.Content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
    }

    private const string CorrelatedSum =
        "SELECT s.SaleId AS SaleId FROM Sales AS s WHERE s.Total > (SELECT SUM(o.Total) FROM Sales AS o WHERE o.CustomerId = s.CustomerId)";

    private const string OneGroup =
        "ctx.Set<Sale>().Where(o => o.CustomerId == s.CustomerId).GroupBy(o => 1).Select(g => (decimal?)g.Sum(o => o.Total)).Max()";

    /// <summary>
    /// A SUM in a correlated subquery is the projection of one group, lifted, taken by Max()
    /// (decision 159), and reads back as the subquery it stands for - so does the form before
    /// that decision, which took the one row by FirstOrDefault().
    /// </summary>
    [Fact]
    public void ASumInACorrelatedSubqueryIsTheProjectionOfOneGroup()
    {
        Assert.Contains($".Where(s => s.Total > {OneGroup})", Text(Sql(new EFCoreLinqQueryBuilder(), CorrelatedSum)), StringComparison.Ordinal);

        Assert.Equal(CorrelatedSum, Text(Linq($"Where(s => s.Total > {OneGroup}).Select(s => new {{ SaleId = s.SaleId }})")));

        var firstRow = OneGroup.Replace(".Max()", ".FirstOrDefault()", StringComparison.Ordinal);
        Assert.Equal(CorrelatedSum, Text(Linq($"Where(s => s.Total > {firstRow}).Select(s => new {{ SaleId = s.SaleId }})")));
    }

    /// <summary>NHibernate's provider writes the bare sum, NULL over no rows already, so its second form keeps the plain Sum.</summary>
    [Fact]
    public void TheSecondFormOfNHibernateKeepsThePlainSum()
    {
        var linq = Text(Sql(new NHibernateLinqQueryBuilder(), CorrelatedSum));

        Assert.Contains("session.Query<Sale>().Where(o => o.CustomerId == s.CustomerId).Sum(o => o.Total)", linq, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupBy", linq, StringComparison.Ordinal);
    }

    /// <summary>
    /// Over a group, a SUM over a value that may be NULL is guarded by the count of the values,
    /// in the projection, the HAVING and over distinct values alike, and reads back as the SUM;
    /// over a value that cannot be NULL the group has one, and the plain Sum stays.
    /// </summary>
    [Theory]
    [InlineData(
        "SELECT s.CustomerId AS CustomerId, SUM(s.Weight) AS Weight FROM Sales AS s GROUP BY s.CustomerId",
        "Weight = (g.Count(s => s.Weight != null) > 0 ? (double?)g.Sum(s => s.Weight) : null)",
        "GroupBy(s => s.CustomerId).Select(g => new { CustomerId = g.Key, Weight = (g.Count(s => s.Weight != null) > 0 ? (double?)g.Sum(s => s.Weight) : null) })")]
    [InlineData(
        "SELECT s.CustomerId AS CustomerId FROM Sales AS s GROUP BY s.CustomerId HAVING SUM(s.Weight) < 1",
        ".Where(g => (g.Count(s => s.Weight != null) > 0 ? (double?)g.Sum(s => s.Weight) : null) < 1)",
        "GroupBy(s => s.CustomerId).Where(g => (g.Count(s => s.Weight != null) > 0 ? (double?)g.Sum(s => s.Weight) : null) < 1).Select(g => new { CustomerId = g.Key })")]
    [InlineData(
        "SELECT s.CustomerId AS CustomerId, SUM(DISTINCT s.Weight) AS Weight FROM Sales AS s GROUP BY s.CustomerId",
        "Weight = (g.Count(s => s.Weight != null) > 0 ? (double?)g.Select(s => s.Weight).Distinct().Sum() : null)",
        "GroupBy(s => s.CustomerId).Select(g => new { CustomerId = g.Key, Weight = (g.Count(s => s.Weight != null) > 0 ? (double?)g.Select(s => s.Weight).Distinct().Sum() : null) })")]
    [InlineData(
        "SELECT s.CustomerId AS CustomerId, SUM(s.Total) AS Total FROM Sales AS s GROUP BY s.CustomerId",
        "Total = g.Sum(s => s.Total)",
        "GroupBy(s => s.CustomerId).Select(g => new { CustomerId = g.Key, Total = g.Sum(s => s.Total) })")]
    public void ASumOverAGroupIsNullWhereTheGroupHasNoValue(string sql, string written, string chain)
    {
        Assert.Contains(written, Text(Sql(new EFCoreLinqQueryBuilder(), sql)), StringComparison.Ordinal);

        Assert.Equal(sql, Text(Linq(chain)));
    }

    /// <summary>COALESCE(SUM(x), 0) is the Sum of EF Core exactly, over a group and over a scalar subquery, and is written as it.</summary>
    [Theory]
    [InlineData(
        "SELECT s.CustomerId AS CustomerId, COALESCE(SUM(s.Weight), 0) AS Weight FROM Sales AS s GROUP BY s.CustomerId",
        "Weight = g.Sum(s => s.Weight) })")]
    [InlineData(
        "SELECT s.SaleId AS SaleId FROM Sales AS s WHERE s.Total > COALESCE((SELECT SUM(o.Total) FROM Sales AS o WHERE o.CustomerId = s.CustomerId), 0.0)",
        ".Where(s => s.Total > ctx.Set<Sale>().Where(o => o.CustomerId == s.CustomerId).Sum(o => o.Total))")]
    public void CoalesceOfASumWithZeroIsThePlainSum(string sql, string written)
    {
        var linq = Text(Sql(new EFCoreLinqQueryBuilder(), sql));

        Assert.Contains(written, linq, StringComparison.Ordinal);
        Assert.DoesNotContain("??", linq, StringComparison.Ordinal);
        Assert.DoesNotContain("GroupBy(o => 1)", linq, StringComparison.Ordinal);
    }

    // ---- the fourth level, over the fixture of the differential matrix ---------------------

    private const string ShopLinks = """
        namespace Shop;

        public class ShopProduct
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
        }

        public class ShopProductLink
        {
            public int LinkId { get; set; }
            public int FromProductId { get; set; }
            public int ToProductId { get; set; }
        }
        """;

    /// <summary>Product 5 has no outgoing link: SQL compares it with NULL and drops it, the Sum of EF Core compared it with 0 and kept it.</summary>
    [Fact]
    public void ACorrelatedSubqueryWithoutRowsIsNull()
    {
        const string sql = "SELECT p.ProductId AS ProductId FROM {{schema}}.ShopProducts AS p "
            + "WHERE p.ProductId >= (SELECT SUM(l.LinkId) FROM {{schema}}.ShopProductLinks AS l WHERE l.FromProductId = p.ProductId)";

        var expected = Direct(sql);
        Assert.Equal(new[] { "1", "2", "6" }, expected);
        Assert.Equal(expected, Translated(ShopLinks, sql, "SumOverNoValues_EmptySubquery", ".GroupBy(l => 1)"));
    }

    /// <summary>
    /// Projected, the sum over no rows is NULL as well: product 5 has none, and the lifted value
    /// Max() takes over the absent group holds the NULL (decision 159).
    /// </summary>
    [Fact]
    public void AProjectedCorrelatedSubqueryWithoutRowsIsNull()
    {
        const string sql = "SELECT p.ProductId AS ProductId, (SELECT SUM(l.LinkId) FROM {{schema}}.ShopProductLinks AS l WHERE l.FromProductId = p.ProductId) AS Links "
            + "FROM {{schema}}.ShopProducts AS p";

        var expected = Direct(sql);
        Assert.Contains("5|NULL", expected);
        Assert.Equal(expected, Translated(ShopLinks, sql, "SumOverNoValues_Projected", ".Max()"));
    }

    /// <summary>The same comparison under COALESCE(…, 0) keeps product 5, and is the plain Sum.</summary>
    [Fact]
    public void ACoalescedSubqueryIsZero()
    {
        const string sql = "SELECT p.ProductId AS ProductId FROM {{schema}}.ShopProducts AS p "
            + "WHERE p.ProductId >= COALESCE((SELECT SUM(l.LinkId) FROM {{schema}}.ShopProductLinks AS l WHERE l.FromProductId = p.ProductId), 0)";

        var expected = Direct(sql);
        Assert.Equal(new[] { "1", "2", "5", "6" }, expected);
        Assert.Equal(expected, Translated(ShopLinks, sql, "SumOverNoValues_Coalesced", ".Sum(l => l.LinkId)"));
    }

    /// <summary>
    /// The shape of FIBEN q207: a group compared with the sum of its correlated rows of another
    /// kind, of which only the group of product 3 has any (its link to product 1).
    /// </summary>
    [Fact]
    public void AGroupComparedWithAnEmptyCorrelatedSumIsDropped()
    {
        const string sql = "SELECT l.FromProductId AS FromProductId FROM {{schema}}.ShopProductLinks AS l GROUP BY l.FromProductId "
            + "HAVING SUM(l.LinkId) > (SELECT SUM(i.LinkId) FROM {{schema}}.ShopProductLinks AS i WHERE i.FromProductId = l.FromProductId AND i.ToProductId = 1)";

        var expected = Direct(sql);
        Assert.Equal(new[] { "3" }, expected);
        Assert.Equal(expected, Translated(ShopLinks, sql, "SumOverNoValues_Having", ".GroupBy(i => 1)"));
    }

    /// <summary>
    /// A column the entity declares not nullable is NULL all the same on the side of a left join
    /// that found no match: product 5 sums no link, and the guard counts the joined rows.
    /// </summary>
    [Fact]
    public void ASumOnTheOptionalSideOfALeftJoinIsNullWithoutAMatch()
    {
        const string sql = "SELECT p.ProductId AS ProductId, SUM(l.LinkId) AS Links FROM {{schema}}.ShopProducts AS p "
            + "LEFT JOIN {{schema}}.ShopProductLinks AS l ON l.FromProductId = p.ProductId GROUP BY p.ProductId";

        var expected = Direct(sql);
        Assert.Contains("5|NULL", expected);
        Assert.Equal(expected, Translated(ShopLinks, sql, "SumOverNoValues_LeftJoin", "g.Count(t => t.l != null) > 0"));
    }

    /// <summary>Product 2 has no weight: its sum is NULL, not 0, in the projection and in HAVING.</summary>
    [Theory]
    [InlineData("SELECT p.ProductId AS ProductId, SUM(p.Weight) AS Weighed FROM {{schema}}.DifferentialProducts AS p GROUP BY p.ProductId", "2|NULL", "Projection")]
    [InlineData("SELECT p.ProductId AS ProductId FROM {{schema}}.DifferentialProducts AS p GROUP BY p.ProductId HAVING SUM(p.Weight) < 1", "4", "Having")]
    public void AGroupWithoutAValueSumsToNull(string sql, string row, string clause)
    {
        var expected = Direct(sql);
        Assert.Contains(row, expected);
        Assert.Equal(expected, Translated(DifferentialData.Read("inputs/dapper/Product.cs"), sql, "SumOverNoValues_NoValue" + clause, "g.Count(p => p.Weight != null) > 0"));
    }

    /// <summary>The rows of the EF Core artifact translated from the Dapper source, after the shape is checked.</summary>
    private List<string> Translated(string entities, string sql, string assembly, string shape)
    {
        fixture.SkipIfUnavailable();

        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new() { Content = entities, ContentType = ConversionContentType.CSharp },
                new() { Content = sql.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.SqlQuery },
            ],
            fixture.CatalogReader);

        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.Contains(shape, method, StringComparison.Ordinal);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            assembly,
            method,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");

        var loaded = Assembly.Load(compiled);
        using var connection = fixture.OpenConnection();
        using var context = EFCoreAcceptance.OpenContext(loaded, connection);

        var query = loaded.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

        var rows = new List<string>();
        foreach (var row in (IEnumerable)query.Invoke(null, [context])!)
        {
            var type = row.GetType();
            rows.Add(type.IsPrimitive || row is decimal or string
                ? Format(row)
                : string.Join("|", type.GetProperties().Select(property => Format(property.GetValue(row)))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>The rows as SQL Server answers the source query itself.</summary>
    private List<string> Direct(string sql)
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{{schema}}", fixture.SchemaName);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Format(reader.IsDBNull(i) ? null : reader.GetValue(i)))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static string Format(object? value) => value is null ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
