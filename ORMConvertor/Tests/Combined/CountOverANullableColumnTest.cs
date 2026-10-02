using System.Collections;
using System.Reflection;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.Differential;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// COUNT over a column counts the column's non-null values, not the rows. The EF Core
/// target used to write it as the group's Count(), with a Convention record saying so - over
/// a nullable column another number, the kind of difference decision 004 forbids. It now
/// counts through a test for null, which EF Core 10.0.10 translates to the COUNT of the
/// column's non-null values, and keeps Count() only where the entity declares the member a
/// value type that cannot be null. Measured at the third level (ToQueryString) and the
/// fourth, over the fixture of the differential matrix, whose Weight is NULL in one row.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class CountOverANullableColumnTest(TestSchemaFixture fixture)
{
    private const string NullableCount =
        "SELECT p.IsDiscontinued AS IsDiscontinued, COUNT(p.Weight) AS Weighed FROM {{schema}}.DifferentialProducts AS p GROUP BY p.IsDiscontinued";

    private const string KeyCount =
        "SELECT p.IsDiscontinued AS IsDiscontinued, COUNT(p.ProductId) AS Products FROM {{schema}}.DifferentialProducts AS p GROUP BY p.IsDiscontinued";

    private ConversionResult FromDapper(string sql) => ConversionHandler.Convert(
        ORMEnum.Dapper,
        ORMEnum.EFCore,
        [
            new() { Content = DifferentialData.Read("inputs/dapper/Product.cs"), ContentType = ConversionContentType.CSharp },
            new() { Content = sql.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.SqlQuery },
        ],
        fixture.CatalogReader);

    private static string Method(ConversionResult result)
        => Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;

    [Fact]
    public void ANullableColumnIsCountedThroughATestForNull()
    {
        fixture.SkipIfUnavailable();

        var result = FromDapper(NullableCount);
        var method = Method(result);

        Assert.Matches(@"g\.Count\((\w+) => \1\.Weight != null\)", method);
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Convention or ConversionRecordKind.Fallback
            && r.Feature == AbstractWrappers.Descriptors.QueryFeature.Aggregation);

        var compiled = Compile(result, method, "CountOverANullableColumn_Nullable");
        Assert.Contains("COUNT(CASE", EFCoreQueryAcceptance.Translate(compiled).Replace("\r\n", " "), StringComparison.OrdinalIgnoreCase);

        Assert.Equal(Direct(NullableCount), Run(compiled, "IsDiscontinued", "Weighed"));
    }

    /// <summary>A key part is never NULL, so the group's Count() counts the same and stays.</summary>
    [Fact]
    public void AColumnThatCannotBeNullIsCountedByCount()
    {
        fixture.SkipIfUnavailable();

        var result = FromDapper(KeyCount);
        var method = Method(result);

        Assert.Contains("g.Count()", method);
        Assert.DoesNotContain("!= null", method);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Convention
            && r.Feature == AbstractWrappers.Descriptors.QueryFeature.Aggregation);

        Assert.Equal(Direct(KeyCount), Run(Compile(result, method, "CountOverANullableColumn_Key"), "IsDiscontinued", "Products"));
    }

    /// <summary>The shape the target writes is read back as the COUNT of the column, so a round trip keeps it.</summary>
    [Fact]
    public void TheTestForNullIsReadBackAsTheCountOfTheColumn()
    {
        const string unit = """
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using Microsoft.EntityFrameworkCore;

            namespace Shop;

            [Table("DifferentialProducts")]
            public class DifferentialProduct
            {
                [Key]
                public int ProductId { get; set; }

                public double? Weight { get; set; }

                public bool IsDiscontinued { get; set; }
            }

            public class Products(DbContext ctx)
            {
                public object Weighed() => ctx.Set<DifferentialProduct>()
                    .GroupBy(p => p.IsDiscontinued)
                    .Select(g => new { IsDiscontinued = g.Key, Weighed = g.Count(e => e.Weight != null) })
                    .ToList();
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [new() { Content = unit, ContentType = ConversionContentType.CSharp, Name = "Products.cs" }]);

        // Dapper has no place for the entity's mapping facts, which go as losses of their own;
        // nothing about the query is lost.
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure
            || (r.Kind == ConversionRecordKind.Loss && r.Feature is not null));
        var sql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;
        Assert.Contains("COUNT(p.Weight) AS Weighed", sql);
    }

    /// <summary>
    /// A column the entity declares as a value type that cannot be null is null all the same
    /// in a row an outer join found no match for: the products of the shared domain counted by
    /// their outgoing links, where product 5 has none and counts 0 - Count() counted the row
    /// of nulls the left join keeps for it and answered 1. The joined row itself is tested.
    /// </summary>
    [Fact]
    public void AKeyOnTheOptionalSideOfALeftJoinIsCountedThroughItsRow()
    {
        fixture.SkipIfUnavailable();

        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new() { Content = ShopLinks, ContentType = ConversionContentType.CSharp },
                new() { Content = LinksPerProduct.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.SqlQuery },
            ],
            fixture.CatalogReader);

        AssertLinksPerProduct(result, "CountOverANullableColumn_DapperLeftJoin");
    }

    /// <summary>The same count from a JPA source, whose join along the collection is derived from the relation (decision 101).</summary>
    [Fact]
    public void AKeyOnTheOptionalSideOfAJoinAlongACollectionIsCountedThroughItsRow()
    {
        fixture.SkipIfUnavailable();

        var result = ConversionHandler.Convert(
            ORMEnum.Hibernate,
            ORMEnum.EFCore,
            [
                new() { Content = JpaProduct.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.Java, Name = "ShopProduct.java" },
                new() { Content = JpaProductLink.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.Java, Name = "ShopProductLink.java" },
                new()
                {
                    Content = "select p.ProductId as ProductId, count(l.LinkId) as Links from ShopProduct p left join p.links l group by p.ProductId",
                    ContentType = ConversionContentType.JpqlQuery,
                },
            ],
            fixture.CatalogReader);

        AssertLinksPerProduct(result, "CountOverANullableColumn_JpaLeftJoin");
    }

    /// <summary>A constant is counted once per row, as COUNT(*) is, so the group's Count() is exact and nothing is tested for null.</summary>
    [Fact]
    public void AConstantIsCountedByCount()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new() { Content = DifferentialData.Read("inputs/dapper/Product.cs"), ContentType = ConversionContentType.CSharp },
                new()
                {
                    Content = "SELECT p.IsDiscontinued AS IsDiscontinued, COUNT(1) AS Products FROM DifferentialProducts AS p GROUP BY p.IsDiscontinued",
                    ContentType = ConversionContentType.SqlQuery,
                },
            ]);

        var method = Method(result);
        Assert.Contains("g.Count()", method);
        Assert.DoesNotContain("!= null", method);
    }

    /// <summary>
    /// A reference to another row is no column: read as one, the test for null over it came
    /// out as <c>COUNT(o.Customer)</c> and <c>o.Customer IS NOT NULL</c>, a column no table
    /// has. The navigation is not read - the projection is dropped with a record and the
    /// filter refused, as any operand the reading cannot take (decision 070).
    /// </summary>
    [Theory]
    [InlineData(".GroupBy(o => o.CustomerId).Select(g => new { CustomerId = g.Key, Placed = g.Count(e => e.Customer != null) })")]
    [InlineData(".Where(o => o.Customer != null).Select(o => new { o.OrderId })")]
    public void ANavigationIsNotReadAsAColumn(string chain)
    {
        var unit = $$"""
            using System.ComponentModel.DataAnnotations;
            using Microsoft.EntityFrameworkCore;

            namespace Shop;

            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                public List<Order> Orders { get; set; }
            }

            public class Order
            {
                [Key]
                public int OrderId { get; set; }

                public int CustomerId { get; set; }

                public Customer Customer { get; set; }
            }

            public class Orders(DbContext ctx)
            {
                public object Query() => ctx.Set<Order>(){{chain}}.ToList();
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [new() { Content = unit, ContentType = ConversionContentType.CSharp, Name = "Orders.cs" }]);

        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery
            && System.Text.RegularExpressions.Regex.IsMatch(s.Content, @"\.Customer\b"));
        Assert.Contains(result.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure
            && r.Reason.Contains("navigation", StringComparison.Ordinal));
    }

    private const string LinksPerProduct =
        "SELECT p.ProductId AS ProductId, COUNT(l.LinkId) AS Links FROM {{schema}}.ShopProducts AS p "
        + "LEFT JOIN {{schema}}.ShopProductLinks AS l ON l.FromProductId = p.ProductId GROUP BY p.ProductId";

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

    private const string JpaProduct = """
        package Shop;

        import jakarta.persistence.*;
        import java.util.List;

        @Entity
        @Table(name = "ShopProducts", schema = "{{schema}}")
        public class ShopProduct {
            @Id
            @Column(name = "ProductId")
            private Integer ProductId;

            @Column(name = "ProductName", nullable = false)
            private String ProductName;

            @OneToMany(mappedBy = "from")
            private List<ShopProductLink> links;
        }
        """;

    private const string JpaProductLink = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "ShopProductLinks", schema = "{{schema}}")
        public class ShopProductLink {
            @Id
            @Column(name = "LinkId")
            private Integer LinkId;

            @ManyToOne
            @JoinColumn(name = "FromProductId")
            private ShopProduct from;

            @Column(name = "ToProductId", nullable = false)
            private Integer ToProductId;
        }
        """;

    private void AssertLinksPerProduct(ConversionResult result, string assembly)
    {
        var method = Method(result);

        Assert.Matches(@"g\.Count\((\w+) => \1(\.\w+)+ != null\)", method);
        Assert.DoesNotContain("g.Count()", method);

        var compiled = Compile(result, method, assembly);
        Assert.Contains("COUNT(CASE", EFCoreQueryAcceptance.Translate(compiled).Replace("\r\n", " "), StringComparison.OrdinalIgnoreCase);

        var expected = Direct(LinksPerProduct);
        Assert.Contains("5:0", expected);
        Assert.Equal(expected, Run(compiled, "ProductId", "Links"));
    }

    private static byte[] Compile(ConversionResult result, string method, string assembly)
        => GeneratedQueryCompiler.CompileOrFail(
            assembly,
            method,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");

    /// <summary>The counts per group as the generated query returns them, ordered by the group.</summary>
    private List<string> Run(byte[] compiled, string key, string counted)
    {
        var assembly = Assembly.Load(compiled);
        using var connection = fixture.OpenConnection();
        using var context = EFCoreAcceptance.OpenContext(assembly, connection);

        var method = assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

        var rows = new List<string>();
        foreach (var row in (IEnumerable)method.Invoke(null, [context])!)
        {
            var type = row.GetType();
            rows.Add($"{type.GetProperty(key)!.GetValue(row)}:{type.GetProperty(counted)!.GetValue(row)}");
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>The counts per group as SQL Server answers the source query itself.</summary>
    private List<string> Direct(string sql)
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{{schema}}", fixture.SchemaName);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add($"{reader.GetValue(0)}:{reader.GetInt32(1)}");
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }
}
