using AbstractWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// NHibernate can write a column as an attribute or as a nested element. For an identifier
/// the nested form is not a stylistic alternative: length, precision and scale have nowhere
/// else to go, so without it a key column loses them in both directions.
/// </summary>
public class NHibernateColumnElementTest
{
    private static string Mapping(string body) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Product" table="Products">
        {body}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder ParseMapping(string mapping)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(mapping);
        return builder;
    }

    [Fact]
    public void NestedColumnOnPropertyIsRead()
    {
        var builder = ParseMapping(Mapping("""
                    <property name="Name" type="String">
                        <column name="ProductName" length="120" not-null="true" />
                    </property>
        """));

        var map = Assert.Single(builder.EntityMap.PropertyMaps);
        Assert.Equal("ProductName", map.ColumnName);
        Assert.Equal(120, map.Length);
        Assert.False(map.IsNullable);
    }

    [Fact]
    public void NestedColumnCarriesPrecisionAndScale()
    {
        var builder = ParseMapping(Mapping("""
                    <property name="Price" type="Decimal">
                        <column name="UnitPrice" precision="18" scale="2" />
                    </property>
        """));

        var map = Assert.Single(builder.EntityMap.PropertyMaps);
        Assert.Equal(18, map.Precision);
        Assert.Equal(2, map.Scale);
    }

    /// <summary>
    /// The nested element is the more specific of the two forms, so it decides.
    /// </summary>
    [Fact]
    public void NestedColumnWinsOverTheAttribute()
    {
        var builder = ParseMapping(Mapping("""
                    <property name="Name" type="String" column="Outer" length="10">
                        <column name="Inner" length="120" />
                    </property>
        """));

        var map = Assert.Single(builder.EntityMap.PropertyMaps);
        Assert.Equal("Inner", map.ColumnName);
        Assert.Equal(120, map.Length);
    }

    [Fact]
    public void NestedColumnOnIdIsRead()
    {
        var builder = ParseMapping(Mapping("""
                    <id name="Code" type="String">
                        <column name="ProductCode" length="10" />
                        <generator class="assigned" />
                    </id>
        """));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        var map = Assert.Single(pk.Parts).PropertyMap;
        Assert.Equal("ProductCode", map.ColumnName);
        Assert.Equal(10, map.Length);
        // "assigned" has no counterpart of its own in the model - it is the absence of a
        // generation strategy, so it maps to None, and None maps back to "assigned".
        Assert.Equal(PrimaryKeyStrategy.None, pk.Parts[0].Strategy);
    }

    [Fact]
    public void NestedColumnOnKeyPropertyIsRead()
    {
        var builder = ParseMapping(Mapping("""
                    <composite-id>
                        <key-property name="Code" type="String">
                            <column name="ProductCode" length="10" />
                        </key-property>
                        <key-property name="Region" type="String" column="RegionCode" />
                    </composite-id>
        """));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(2, pk.Parts.Count);

        Assert.Equal("ProductCode", pk.Parts[0].PropertyMap.ColumnName);
        Assert.Equal(10, pk.Parts[0].PropertyMap.Length);

        // The attribute form still works next to the nested one.
        Assert.Equal("RegionCode", pk.Parts[1].PropertyMap.ColumnName);
        Assert.Null(pk.Parts[1].PropertyMap.Length);
    }

    /// <summary>
    /// A key with nothing to put in a nested element keeps the compact form. This is what
    /// holds every existing generated mapping unchanged.
    /// </summary>
    [Fact]
    public void KeyWithoutFacetsKeepsTheAttributeForm()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("Products");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<id name=\"Id\" column=\"Id\"", xml);
        Assert.DoesNotContain("<column", xml);
    }

    [Fact]
    public void KeyWithLengthIsWrittenAsNestedColumn()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("Products");
        builder.AddProperty("string", "Code", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.None, "Code");
        builder.SetPropertyDatabaseMapping("Code", new() { ["column"] = "ProductCode", ["length"] = "10" });

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<column name=\"ProductCode\" length=\"10\" />", xml);
        Assert.DoesNotContain("<id name=\"Code\" column=", xml);

        // The generator has to stay inside <id>, after the column.
        Assert.True(
            xml.IndexOf("<column name=\"ProductCode\"") < xml.IndexOf("<generator"),
            "NHibernate expects the column before the generator.");
    }

    /// <summary>
    /// The case that motivated the whole item: a length declared on a key in EF Core used to
    /// disappear on the way into an NHibernate mapping.
    /// </summary>
    [Fact]
    public void KeyLengthSurvivesTranslationFromEFCore()
    {
        const string source = """
            namespace EFCoreEntities;

            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Products")]
            public class Product
            {
                [Key]
                [Column("ProductCode")]
                [MaxLength(10)]
                public required string Code { get; set; }
            }
            """;

        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("length=\"10\"", xml);
        Assert.Contains("name=\"ProductCode\"", xml);
    }

    /// <summary>
    /// And back again, so the facet is not merely written but readable.
    /// </summary>
    [Fact]
    public void KeyLengthSurvivesTheNHibernateRoundTrip()
    {
        // NHibernate splits a mapping over two artifacts: the class carries the language
        // type, the descriptor the database facts. Generating from the descriptor alone is
        // not a supported input, so the round trip starts from both.
        const string entity = """
            public class Product
            {
                public virtual string Code { get; set; }
            }
            """;

        var first = new NHibernateEntityBuilder();
        new NHibernateEntityParser(first).Parse(entity);
        new NHibernateXMLMappingParser(first).Parse(Mapping("""
                    <id name="Code" type="String">
                        <column name="ProductCode" length="10" />
                        <generator class="assigned" />
                    </id>
        """));

        var xml = first.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        var second = ParseMapping(xml);

        var map = Assert.Single(second.EntityMap.PrimaryKey!.Parts).PropertyMap;
        Assert.Equal("ProductCode", map.ColumnName);
        Assert.Equal(10, map.Length);
    }
}