using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Covers the primary key as a concept of the intermediate representation: a simple
/// key together with its generation strategy, the invariants the model enforces on
/// key parts, and what a builder does with mapping facts the source framework does
/// not state. Translation of composite keys themselves is covered by
/// <see cref="CompositeKeyTest"/>.
/// </summary>
public class PrimaryKeyTest
{
    private const string SimpleKeyXmlMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers" schema="Sales">
                <id name="CustomerID" column="CustomerID" type="int">
                    <generator class="sequence" />
                </id>
                <property name="Name" column="Name" type="string" />
            </class>
        </hibernate-mapping>
        """;

    private const string EFCoreEntityWithoutColumnMetadata = """
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public required int CustomerID { get; set; }

            public required string Name { get; set; }
        }
        """;

    [Fact]
    public void SimpleKeyIsParsedAsSinglePartWithItsStrategy()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        parser.Parse(SimpleKeyXmlMapping);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        // A simple key is not a separate case in the model - it is a key with one part.
        // Everything a composite key can express is available here too, just unused.
        var part = Assert.Single(pk.Parts);
        Assert.Equal("CustomerID", part.PropertyMap.Property.Name);
        Assert.Equal(1, part.Order);
        Assert.Equal(PrimaryKeyStrategy.Sequence, part.Strategy);
    }

    [Theory]
    [InlineData(PrimaryKeyStrategy.None, "assigned")]
    [InlineData(PrimaryKeyStrategy.Increment, "increment")]
    [InlineData(PrimaryKeyStrategy.Identity, "identity")]
    [InlineData(PrimaryKeyStrategy.Sequence, "sequence")]
    [InlineData(PrimaryKeyStrategy.HiLo, "hilo")]
    [InlineData(PrimaryKeyStrategy.Uuid, "uuid")]
    [InlineData(PrimaryKeyStrategy.Guid, "guid")]
    public void KeyStrategySurvivesRoundTripThroughModel(PrimaryKeyStrategy strategy, string expectedGenerator)
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "CustomerID");
        builder.AddPrimaryKey(strategy, "CustomerID");

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.Contains($"<generator class=\"{expectedGenerator}\" />", xml);

        // And back again. The strategy is a framework-agnostic enum, not a string
        // carried through untouched - both directions of the conversion are exercised
        // here, which is what makes the strategy portable to another framework.
        var reparsed = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(reparsed).Parse(xml);

        var pk = reparsed.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(strategy, Assert.Single(pk.Parts).Strategy);
    }

    [Fact]
    public void KeyPartsAreSortedByDeclaredOrder()
    {
        // Deliberately handed over in the wrong sequence. Order decides, not the
        // position in the list, and the model sorts on every construction path -
        // so a builder may iterate Parts as they are.
        var key = new PrimaryKey
        {
            Parts =
            [
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("LineNumber"), Order = 2 },
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("OrderID"), Order = 1 },
            ]
        };

        Assert.Equal(2, key.Parts.Count);
        Assert.Equal("OrderID", key.Parts[0].PropertyMap.Property.Name);
        Assert.Equal(1, key.Parts[0].Order);
        Assert.Equal("LineNumber", key.Parts[1].PropertyMap.Property.Name);
        Assert.Equal(2, key.Parts[1].Order);
    }

    [Fact]
    public void KeyWithoutPartsIsRejected()
    {
        // The model refuses it ...
        Assert.Throws<ArgumentException>(() => _ = new PrimaryKey { Parts = [] });

        // ... and so does the builder entry point, so no parser can produce a key
        // that exists but identifies nothing.
        var builder = new DummyEntityBuilder();
        Assert.Throws<ArgumentException>(() => builder.AddPrimaryKey([]));
    }

    [Fact]
    public void EachKeyPartCarriesItsOwnStrategy()
    {
        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("int", "LineNumber");

        // The strategy belongs to the part, not to the key: one part may be
        // generated by the database while the rest is assigned by the application.
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Identity),
            ("LineNumber", 2, PrimaryKeyStrategy.None),
        ]);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(PrimaryKeyStrategy.Identity, pk.Parts[0].Strategy);
        Assert.Equal(PrimaryKeyStrategy.None, pk.Parts[1].Strategy);

        // Key parts point at the existing property mappings, they do not duplicate them.
        Assert.Equal(2, builder.EntityMap.PropertyMaps.Count);
    }

    [Fact]
    public void KeyColumnUnknownToSourceFrameworkFallsBackToConventions()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(EFCoreEntityWithoutColumnMetadata);

        // The source states neither the column name nor the database type of the key.
        // The IR records that absence instead of inventing a value - this is exactly
        // the gap that database metadata is meant to close later (F4, F5).
        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        var keyMap = Assert.Single(pk.Parts).PropertyMap;
        Assert.Null(keyMap.ColumnName);
        Assert.Null(keyMap.Type);

        // Until then the builder falls back to conventions: the property name as the
        // column and a database type guessed from the CLR type. See the TODO in
        // NHibernateEntityBuilder.ResolveNhType for the place the database will fill in.
        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("<id name=\"CustomerID\" column=\"CustomerID\" type=\"Int32\">", xml);

        // The strategy is a convention as well - EF Core generates an int key by
        // default, the attribute itself says nothing about it.
        Assert.Contains("<generator class=\"identity\" />", xml);
    }

    private static PropertyMap NewPropertyMap(string propertyName) => new()
    {
        Property = new Property
        {
            Name = propertyName,
            Type = new CLRTypeModel { CLRType = CLRType.Int },
        }
    };
}