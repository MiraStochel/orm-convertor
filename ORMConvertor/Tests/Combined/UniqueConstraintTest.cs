using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// A unique constraint is a mapping fact the model carries and both annotation targets
/// write (decision 055): [Index(…, IsUnique = true)] in EF Core, unique and unique-key in
/// NHibernate. Dapper is the control - it has nowhere to put one, so it must say so
/// instead of dropping it in silence.
/// </summary>
public class UniqueConstraintTest
{
    private const string ProductSource = """
        public class Product
        {
            public int ProductId { get; set; }

            public string Sku { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
        }
        """;

    private static T Parsed<T>(T builder) where T : AbstractEntityBuilder
    {
        new DapperEntityParser(builder).Parse(ProductSource);
        builder.AddTable("Products");
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "ProductId");
        return builder;
    }

    private static string CodeOf(AbstractEntityBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.CSharpEntity).Content;

    private static string MappingOf(AbstractEntityBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.XML).Content;

    /// <summary>
    /// The one line of the mapping that writes the named property. Asserting against the
    /// line rather than a whole element keeps these tests about uniqueness: the type and
    /// nullability attributes beside it belong to other decisions and change with them.
    /// </summary>
    private static string PropertyLine(string mapping, string propertyName)
        => Assert.Single(
            mapping.Split(Environment.NewLine).Select(line => line.Trim()),
            line => line.StartsWith($"<property name=\"{propertyName}\"", StringComparison.Ordinal));

    [Fact]
    public void EFCoreWritesTheConstraintAsAClassLevelIndexAnnotation()
    {
        var builder = Parsed(new EFCoreEntityBuilder());
        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);

        var code = CodeOf(builder);

        Assert.Contains("[Index(nameof(Sku), IsUnique = true, Name = \"UQ_Products_Sku\")]", code, StringComparison.Ordinal);

        // [Index] lives in the EF Core namespace, so the import has to follow the annotation.
        Assert.Contains("using Microsoft.EntityFrameworkCore;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnnamedConstraintIsWrittenWithoutInventingAName()
    {
        var builder = Parsed(new EFCoreEntityBuilder());
        builder.AddUniqueConstraint(null, ["Sku"]);

        Assert.Contains("[Index(nameof(Sku), IsUnique = true)]", CodeOf(builder), StringComparison.Ordinal);
    }

    [Fact]
    public void NHibernateWritesOneColumnAsUniqueAndSeveralAsUniqueKey()
    {
        var single = Parsed(new NHibernateEntityBuilder());
        single.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);

        Assert.EndsWith("unique=\"true\" />", PropertyLine(MappingOf(single), "Sku"), StringComparison.Ordinal);

        var composite = Parsed(new NHibernateEntityBuilder());
        composite.AddUniqueConstraint("UQ_Products_SkuName", ["Sku", "Name"]);

        var mapping = MappingOf(composite);

        Assert.EndsWith("unique-key=\"UQ_Products_SkuName\" />", PropertyLine(mapping, "Sku"), StringComparison.Ordinal);
        Assert.EndsWith("unique-key=\"UQ_Products_SkuName\" />", PropertyLine(mapping, "Name"), StringComparison.Ordinal);

        // The property the constraint does not cover stays untouched.
        Assert.DoesNotContain("unique", PropertyLine(MappingOf(single), "Name"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnnamedGroupGetsADerivedTokenAndOneConventionRecord()
    {
        var builder = Parsed(new NHibernateEntityBuilder());
        builder.AddUniqueConstraint(null, ["Sku", "Name"]);

        var mapping = MappingOf(builder);

        Assert.Contains("unique-key=\"UQ_Product_Sku_Name\"", mapping, StringComparison.Ordinal);

        // The token is one decision of the tool, however many elements carry its result.
        var conventions = builder.Records
            .Where(r => r.Kind == ConversionRecordKind.Convention && r.Category == MappingFactCategory.UniqueConstraint)
            .ToList();

        Assert.Single(conventions);
    }

    [Fact]
    public void DapperReportsTheConstraintAsALoss()
    {
        var builder = Parsed(new DapperEntityBuilder());
        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);
        builder.Build();

        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.UniqueConstraint);

        // A single-column constraint concerns that property, and the record says which.
        Assert.Equal("Sku", loss.Property);
    }

    [Fact]
    public void NHibernateDropsAConstraintItCannotPlaceOnAPropertyElement()
    {
        var builder = Parsed(new NHibernateEntityBuilder());

        // The identifier is written as <id>, which carries no unique attribute - and a key
        // part is unique by definition, so nothing is claimed by dropping it.
        builder.AddUniqueConstraint("UQ_Products_ProductId", ["ProductId"]);

        var mapping = MappingOf(builder);

        Assert.DoesNotContain("unique", mapping, StringComparison.Ordinal);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property == "ProductId");
    }

    [Fact]
    public void TheConstraintSurvivesEFCoreToNHibernateAndBack()
    {
        const string annotated = """
            using Microsoft.EntityFrameworkCore;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Products")]
            [Index(nameof(Sku), IsUnique = true, Name = "UQ_Products_Sku")]
            public class Product
            {
                [Key]
                public int ProductId { get; set; }

                public string Sku { get; set; } = string.Empty;
            }
            """;

        var toNHibernate = new NHibernateEntityBuilder();
        new EFCoreEntityParser(toNHibernate).Parse(annotated);

        var mapping = MappingOf(toNHibernate);
        Assert.Contains("unique=\"true\"", mapping, StringComparison.Ordinal);

        // Back the other way: what NHibernate wrote is what its own parser reads.
        var backToEFCore = new EFCoreEntityBuilder();
        new NHibernateXMLMappingParser(backToEFCore).Parse(mapping);

        var constraint = Assert.Single(backToEFCore.EntityMaps.Single().UniqueConstraints);
        Assert.Equal(["Sku"], constraint.PropertyNames);

        // unique="true" states the constraint without naming it, so the name does not
        // survive the round trip - and none is invented in its place.
        Assert.Null(constraint.Name);
    }

    [Fact]
    public void NHibernateGroupsTheColumnsOfOneUniqueKeyIntoOneConstraint()
    {
        const string mapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
              <class name="Product" table="Products">
                <id name="ProductId">
                  <generator class="identity" />
                </id>
                <property name="Sku" unique-key="UQ_Products_SkuName" />
                <property name="Name" unique-key="UQ_Products_SkuName" />
              </class>
            </hibernate-mapping>
            """;

        var builder = new EFCoreEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);

        var constraint = Assert.Single(builder.EntityMaps.Single().UniqueConstraints);

        Assert.Equal("UQ_Products_SkuName", constraint.Name);
        Assert.Equal(["Sku", "Name"], constraint.PropertyNames);
    }

    [Fact]
    public void TheSameSetIsNotAddedTwiceAndADifferingNameIsAConflict()
    {
        var builder = Parsed(new EFCoreEntityBuilder());

        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);
        builder.AddUniqueConstraint("UQ_Sku", ["Sku"]);

        var constraint = Assert.Single(builder.EntityMap.UniqueConstraints);

        // A fact read earlier is never overwritten by a later source (decision 017).
        Assert.Equal("UQ_Products_Sku", constraint.Name);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.UniqueConstraint);
    }

    [Fact]
    public void ANamelessSetIsCompletedByALaterNameWithoutAConflict()
    {
        var builder = Parsed(new EFCoreEntityBuilder());

        builder.AddUniqueConstraint(null, ["Sku"]);
        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);

        var constraint = Assert.Single(builder.EntityMap.UniqueConstraints);

        Assert.Equal("UQ_Products_Sku", constraint.Name);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void AConstraintIsIdentifiedByItsSetWhateverTheOrder()
    {
        var first = new UniqueConstraint { PropertyNames = ["Sku", "Name"] };
        var second = new UniqueConstraint { Name = "other", PropertyNames = ["Name", "Sku"] };

        Assert.True(first.CoversSameAs(second));
    }

    [Fact]
    public void AConstraintOverNothingOrOverOnePropertyTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new UniqueConstraint { PropertyNames = [] });
        Assert.Throws<ArgumentException>(() => new UniqueConstraint { PropertyNames = ["Sku", "Sku"] });
    }
}
