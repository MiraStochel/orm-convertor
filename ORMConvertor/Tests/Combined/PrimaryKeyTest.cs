using AbstractWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// Covers the primary key as a concept of the intermediate representation: a simple
/// key together with its generation strategy, the invariants the model enforces on key
/// parts and on the record of a key class used by the source, and what a builder does
/// with mapping facts the source framework does not state. Translation of composite
/// keys themselves is covered by <see cref="CompositeKeyTest"/>.
/// </summary>
public class PrimaryKeyTest
{
    private const string SimpleKeyXmlMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers" schema="Sales">
                <id name="CustomerID" column="CustomerId" type="int">
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

        // The <id> element states the column and the type, so the key part carries
        // them the same way a <key-property> of a composite key does.
        Assert.Equal("CustomerId", part.PropertyMap.ColumnName);
        Assert.Equal(DatabaseType.Integer, part.PropertyMap.Type);
    }

    [Theory]
    [InlineData(PrimaryKeyStrategy.Assigned, "assigned")]
    [InlineData(PrimaryKeyStrategy.Auto, "native")]
    [InlineData(PrimaryKeyStrategy.Identity, "identity")]
    [InlineData(PrimaryKeyStrategy.Sequence, "sequence")]
    [InlineData(PrimaryKeyStrategy.HiLo, "hilo")]
    [InlineData(PrimaryKeyStrategy.Uuid, "guid")]
    [InlineData(PrimaryKeyStrategy.Increment, "increment")]
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
    public void UnspecifiedStrategyFallsBackToTheConventionOfTheTarget()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "CustomerID");
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "CustomerID");

        // NHibernate needs a generator, so the builder writes the convention of the target
        // rather than a fact of the source (decision 008) - and that the source said nothing
        // does not survive the round trip. Reporting the difference is what diagnostics is for.
        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.Contains("<generator class=\"assigned\" />", xml);

        var reparsed = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(reparsed).Parse(xml);

        Assert.Equal(
            PrimaryKeyStrategy.Assigned,
            Assert.Single(reparsed.EntityMap.PrimaryKey!.Parts).Strategy);
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
            ("LineNumber", 2, PrimaryKeyStrategy.Assigned),
        ]);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);
        Assert.Equal(PrimaryKeyStrategy.Identity, pk.Parts[0].Strategy);
        Assert.Equal(PrimaryKeyStrategy.Assigned, pk.Parts[1].Strategy);

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

        // The strategy is a convention as well - EF Core generates an int key by default and
        // the attribute says nothing about how. That is the claim "the framework picks", whose
        // counterpart in NHibernate is native, not identity.
        Assert.Contains("<generator class=\"native\" />", xml);
    }

    [Fact]
    public void SimpleKeyKeepsColumnNameAndDatabaseType()
    {
        const string entitySource = """
            namespace EFCoreEntities;

            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Customers", Schema = "Sales")]
            public class Customer
            {
                [Key]
                [Column("CustomerId", TypeName = "int")]
                public required int CustomerID { get; set; }

                public required string Name { get; set; }
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(entitySource);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        // The mapping is recorded before the key is declared, and the key part picks
        // up the existing property mapping - so a simple key carries the same set of
        // facts as a composite one.
        var part = Assert.Single(pk.Parts);
        Assert.Equal("CustomerID", part.PropertyMap.Property.Name);
        Assert.Equal("CustomerId", part.PropertyMap.ColumnName);
        Assert.Equal(DatabaseType.Integer, part.PropertyMap.Type);
        Assert.Equal(ScalarType.Int, part.PropertyMap.Property.Type!.ScalarType);
        Assert.Equal(PrimaryKeyStrategy.Auto, part.Strategy);
    }

    [Fact]
    public void KeyClassOfTheSourceIsRecordedOnlyWhenTheSourceHasOne()
    {
        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("int", "LineNumber");

        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Assigned),
            ("LineNumber", 2, PrimaryKeyStrategy.Assigned),
        ],
        new SourceKeyClass("OrderLineId", KeyClassForm.Embedded, "Id"));

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        // The class is recorded next to the key, not instead of it: the parts remain the
        // definition of the key, so a target rendering it flat needs nothing else.
        Assert.Equal(new[] { "OrderID", "LineNumber" }, pk.Parts.Select(p => p.PropertyMap.Property.Name));

        var keyClass = pk.SourceKeyClass;
        Assert.NotNull(keyClass);
        Assert.Equal("OrderLineId", keyClass.ClassName);
        Assert.Equal(KeyClassForm.Embedded, keyClass.Form);
        Assert.Equal("Id", keyClass.PropertyName);

        // Absence says something too: the source declared the key parts on the entity
        // itself, so there is no class name to carry over.
        var plain = new DummyEntityBuilder();
        plain.AddClassHeader("public", "OrderLine");
        plain.AddProperty("int", "OrderID");
        plain.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        Assert.Null(plain.EntityMap.PrimaryKey?.SourceKeyClass);
    }

    [Fact]
    public void KeyClassFormDecidesWhetherAPropertyNameBelongsThere()
    {
        // Embedded means the parts are reached through a property of the entity
        // (o.Id.OrderID), so without its name the record would not describe the source.
        Assert.Throws<ArgumentException>(() => _ = new SourceKeyClass("OrderLineId", KeyClassForm.Embedded));
        Assert.Throws<ArgumentException>(() => _ = new SourceKeyClass("OrderLineId", KeyClassForm.Embedded, "   "));

        // Mirrored means the parts stay on the entity (o.OrderID) and no such property
        // exists - accepting one would record a claim about the source that is not true.
        Assert.Throws<ArgumentException>(() => _ = new SourceKeyClass("OrderLineId", KeyClassForm.Mirrored, "Id"));

        // The class name is what the record exists for, so it is required in both forms.
        Assert.Throws<ArgumentException>(() => _ = new SourceKeyClass("", KeyClassForm.Mirrored));
    }

    [Fact]
    public void KeyClassSignalDoesNotChangeGeneratedArtifacts()
    {
        var keyClass = new SourceKeyClass("OrderLineId", KeyClassForm.Embedded, "Id");

        // Every target renders the key flat (decision 006), so the signal must not reach
        // the output. Once a builder does read it - the JPA one will, to name the ID class
        // instead of deriving it by convention - this is the test that has to be revisited
        // deliberately rather than quietly going red.
        Assert.Equal(
            CompositeKeyOutputs(new NHibernateEntityBuilder(), null),
            CompositeKeyOutputs(new NHibernateEntityBuilder(), keyClass));

        Assert.Equal(
            CompositeKeyOutputs(new EFCoreEntityBuilder(), null),
            CompositeKeyOutputs(new EFCoreEntityBuilder(), keyClass));
    }

    [Fact]
    public void KeyPartsMustHaveDistinctOrder()
    {
        // Two parts claiming the same position leave the resulting order to the input
        // rather than to the model - the non-determinism S2 rules out (decision 011).
        Assert.Throws<ArgumentException>(() => _ = new PrimaryKey
        {
            Parts =
            [
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("OrderID"), Order = 1 },
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("LineNumber"), Order = 1 },
            ]
        });

        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("int", "LineNumber");

        Assert.Throws<ArgumentException>(() => builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Identity),
            ("LineNumber", 1, PrimaryKeyStrategy.Identity),
        ]));
    }

    [Fact]
    public void KeyPartOrderNeedNotStartAtOneOrBeContiguous()
    {
        // Only the relative order carries meaning, and sources rarely number the parts at all:
        // EF Core takes it from the argument order of [PrimaryKey(...)], NHibernate from the order
        // of the <key-property> elements. The numbers arise here, so contiguity is not ours to demand.
        var key = new PrimaryKey
        {
            Parts =
            [
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("LineNumber"), Order = 40 },
                new PrimaryKeyPart { PropertyMap = NewPropertyMap("OrderID"), Order = 0 },
            ]
        };

        Assert.Equal(new[] { "OrderID", "LineNumber" }, key.Parts.Select(p => p.PropertyMap.Property.Name));
    }

    [Fact]
    public void StrategyDetailsOfTheSourceAreRecordedOnTheKeyPart()
    {
        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("int", "LineNumber");
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Sequence),
            ("LineNumber", 2, PrimaryKeyStrategy.Identity),
        ],
        new SourceKeyClass("OrderLineId", KeyClassForm.Mirrored));

        builder.SetKeyStrategyDetails(
            "OrderID",
            sourceStrategyName: "seqhilo",
            parameters: new Dictionary<string, string> { ["sequence"] = "order_line_seq", ["max_lo"] = "50" });

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        // The parameters are what keeps a sequence-backed key runnable in the target: without
        // the sequence name the generated mapping points at a sequence that need not exist.
        var detailed = pk.Parts.Single(p => p.PropertyMap.Property.Name == "OrderID");
        Assert.Equal(PrimaryKeyStrategy.Sequence, detailed.Strategy);
        Assert.Equal("seqhilo", detailed.SourceStrategyName);
        Assert.Equal("order_line_seq", detailed.StrategyParameters["sequence"]);
        Assert.Equal("50", detailed.StrategyParameters["max_lo"]);

        // Rebuilding the key around one part must leave everything else as it was.
        var untouched = pk.Parts.Single(p => p.PropertyMap.Property.Name == "LineNumber");
        Assert.Equal(PrimaryKeyStrategy.Identity, untouched.Strategy);
        Assert.Null(untouched.SourceStrategyName);
        Assert.Empty(untouched.StrategyParameters);
        Assert.Equal(new[] { 1, 2 }, pk.Parts.Select(p => p.Order));
        Assert.Equal("OrderLineId", pk.SourceKeyClass?.ClassName);
    }

    [Fact]
    public void StrategyDetailsNeedAKeyPartToLandOn()
    {
        var builder = new DummyEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("string", "Description");

        // No key defined yet ...
        Assert.Throws<InvalidOperationException>(() => builder.SetKeyStrategyDetails("OrderID", "increment"));

        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        // ... and a property outside the key is a mistake, not a silent no-op.
        Assert.Throws<ArgumentException>(() => builder.SetKeyStrategyDetails("Description", "increment"));
    }

    /// <summary>
    /// The same two-part key every time, so the key class signal is the only thing that
    /// can differ between two outputs of the same builder.
    /// </summary>
    private static List<string> CompositeKeyOutputs(AbstractEntityBuilder builder, SourceKeyClass? sourceKeyClass)
    {
        builder.AddClassHeader("public", "OrderLine");
        builder.AddTable("OrderLines");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "LineNumber", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Assigned),
            ("LineNumber", 2, PrimaryKeyStrategy.Assigned),
        ],
        sourceKeyClass);

        return [.. builder.Build().Select(o => o.Content)];
    }

    private static PropertyMap NewPropertyMap(string propertyName) => new()
    {
        Property = new Property
        {
            Name = propertyName,
            Type = LangType.Scalar(ScalarType.Int),
        }
    };
}