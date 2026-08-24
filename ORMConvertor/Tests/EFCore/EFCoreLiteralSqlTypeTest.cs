using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.EFCore;

/// <summary>
/// The escape path of decision 019 used to be one-way: the NHibernate builder wrote the
/// literal type back out as sql-type, the EF Core builder never read it, so one model got two
/// answers and [Column(TypeName="money")] came back as decimal - a different column type, and
/// without a record, because DatabaseType is expressible for EF Core and the mechanical loss
/// reporting therefore stayed quiet (decision 052).
/// </summary>
public class EFCoreLiteralSqlTypeTest
{
    private static string GeneratedEntity(string source)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);
        return builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;
    }

    [Fact]
    public void TheLiteralTypeOfTheSourceReachesTheAnnotation()
    {
        const string entity = """
            using System.ComponentModel.DataAnnotations.Schema;

            public class Order
            {
                public int Id { get; set; }

                [Column("Total", TypeName = "money")]
                public decimal Total { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(entity);

        // Read as the Decimal family with the literal spelling kept beside it (decision 019).
        var map = builder.EntityMap.PropertyMaps.Single(pm => pm.Property.Name == "Total");
        Assert.Equal(DatabaseType.Decimal, map.Type);
        Assert.Equal("money", map.SourceSqlType);

        var code = builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

        Assert.Contains("TypeName=\"money\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeName=\"decimal\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeWithNoFamilyStillReachesTheAnnotation()
    {
        var code = GeneratedEntity("""
            using System.ComponentModel.DataAnnotations.Schema;

            public class Place
            {
                public int Id { get; set; }

                [Column("Area", TypeName = "geography")]
                public object Area { get; set; }
            }
            """);

        // No family at all: before this the annotation carried the column name and nothing
        // else, so the type the source stated vanished from the artifact entirely.
        Assert.Contains("TypeName=\"geography\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVersionColumnKeepsItsAnnotationInsteadOfAColumnType()
    {
        var code = GeneratedEntity("""
            using System.ComponentModel.DataAnnotations;

            public class Product
            {
                public int Id { get; set; }

                [Timestamp]
                public byte[] RowVersion { get; set; }
            }
            """);

        // [Timestamp] states the store type itself; a TypeName beside it would override the
        // rowversion mapping with plain varbinary, so the literal path stays out of its way.
        Assert.Contains("[Timestamp]", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TypeName=", code, StringComparison.Ordinal);
    }
}
