using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// The dissolution phase of decision 031: the name in SourceKeyClass refers to a class of
/// the same conversion, and what that class declares are the key parts, not the properties
/// of another entity. These tests drive the phase through the NHibernate parsers - the pair
/// of an entity class holding the key class in one property and a mapping in the Embedded
/// form - and check the three record kinds the decision prescribes, plus the shape the
/// phase deliberately leaves record-free.
/// </summary>
public class NHibernateEmbeddedKeyClassTest
{
    private const string EntitySource = """
        namespace KeyClassEntities;

        public class OrderLine
        {
            public virtual OrderLineId Id { get; set; } = null!;

            public virtual int Quantity { get; set; }
        }
        """;

    private const string KeyClassSource = """
        namespace KeyClassEntities;

        [Serializable]
        public class OrderLineId
        {
            public virtual int OrderID { get; set; }

            public virtual int LineNo { get; set; }

            public override bool Equals(object? obj) => obj is OrderLineId other
                && other.OrderID == OrderID && other.LineNo == LineNo;

            public override int GetHashCode() => HashCode.Combine(OrderID, LineNo);
        }
        """;

    private const string EmbeddedMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="KeyClassEntities">
            <class name="OrderLine" table="OrderLines">
                <composite-id name="Id" class="OrderLineId">
                    <key-property name="OrderID" column="OrderID" type="int" />
                    <key-property name="LineNo" column="LineNo" type="int" />
                </composite-id>
                <property name="Quantity" column="Quantity" type="int" />
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder Parse(params string[] csharpSources)
        => ParseWith(EmbeddedMapping, csharpSources);

    private static NHibernateEntityBuilder ParseWith(string mapping, params string[] csharpSources)
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);

        foreach (var source in csharpSources)
        {
            entityParser.Parse(source);
        }

        new NHibernateXMLMappingParser(builder).Parse(mapping);
        return builder;
    }

    [Fact]
    public void KeyClassDissolvesIntoTheKeyOfTheEntityThatNamesIt()
    {
        var builder = Parse(EntitySource, KeyClassSource);
        var outputs = builder.Build();

        // The class is no entity of the conversion (rule E1): one entity remains and the
        // parts carry what the class declared about its members - the column side stays
        // as the mapping read it.
        var entityMap = Assert.Single(builder.EntityMaps);
        Assert.Equal("OrderLine", entityMap.Entity.Name);

        var parts = entityMap.PrimaryKey!.Parts;
        Assert.Equal(new[] { "OrderID", "LineNo" }, parts.Select(p => p.PropertyMap.Property.Name));
        Assert.All(parts, p =>
        {
            Assert.Equal(ScalarType.Int, p.PropertyMap.Property.Type?.ScalarType);
            Assert.Equal(AccessModifier.Public, p.PropertyMap.Property.AccessModifier);
            Assert.True(p.PropertyMap.Property.HasGetter);
            Assert.True(p.PropertyMap.Property.HasSetter);
        });
        Assert.Equal(new[] { "OrderID", "LineNo" }, parts.Select(p => p.PropertyMap.ColumnName));

        // The holding property is not a property of the entity - the flat rendering
        // replaces it with the parts themselves.
        Assert.DoesNotContain(entityMap.Entity.Properties, p => p.Name == "Id");

        // The artifacts come out flat and the class name reaches neither of them.
        var code = outputs.Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;
        var xml = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;
        Assert.DoesNotContain("OrderLineId", code);
        Assert.Contains("OrderID", code);
        Assert.DoesNotContain("OrderLineId", xml);
        Assert.Contains("<composite-id>", xml);

        // The change of form is a loss: the class name and the access path through 'Id'
        // disappear from the output (decisions 006 and 031).
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Id" && r.Reason.Contains("OrderLineId"));
    }

    [Fact]
    public void OrderOfTheInputSourcesDoesNotChangeTheResult()
    {
        // The phase resolves against the complete set of sources, so the key class before
        // the entity and after it must end in the same artifacts (S2).
        var first = Parse(EntitySource, KeyClassSource).Build().Select(o => o.Content).ToList();
        var second = Parse(KeyClassSource, EntitySource).Build().Select(o => o.Content).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void MissingKeyClassEndsInRecordsNotACrash()
    {
        var builder = Parse(EntitySource);
        var outputs = builder.Build();

        // Nothing declares the named class, so the parts have no language type and the
        // completeness gate refuses the entity; the phase's record says why.
        Assert.Empty(outputs);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("OrderLineId"));
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Failure && r.Property == "OrderID");
    }

    [Fact]
    public void KeyClassWithItsOwnMappingStaysAnEntityAndTheDisagreementIsAConflict()
    {
        const string keyClassMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="KeyClassEntities">
                <class name="OrderLineId" table="OrderLineIds">
                    <id name="OrderID" column="OrderID" type="int">
                        <generator class="assigned" />
                    </id>
                </class>
            </hibernate-mapping>
            """;

        var builder = Parse(EntitySource, KeyClassSource);
        new NHibernateXMLMappingParser(builder).Parse(keyClassMapping);
        builder.Build();

        // Two first-degree sources claim two different things about the class; silently
        // un-entitying it would be worse than an unread key class (decision 031).
        Assert.Contains(builder.EntityMaps, em => em.Entity.Name == "OrderLineId");
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Entity == "OrderLineId");
    }

    [Fact]
    public void KeyClassMemberTheMappingDoesNotNameIsALoss()
    {
        const string keyClassWithExtraMember = """
            namespace KeyClassEntities;

            [Serializable]
            public class OrderLineId
            {
                public virtual int OrderID { get; set; }

                public virtual int LineNo { get; set; }

                public virtual string Checksum { get; set; } = string.Empty;
            }
            """;

        var builder = Parse(EntitySource, keyClassWithExtraMember);
        builder.Build();

        // The mapping does not persist the member, so it becomes neither a key part nor
        // a property of the entity - and the drop is said, not silent (decision 031).
        var entityMap = Assert.Single(builder.EntityMaps);
        Assert.DoesNotContain(entityMap.Entity.Properties, p => p.Name == "Checksum");
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Checksum");
    }

    [Fact]
    public void MirroredClassNobodyDeclaresOverTypedPartsIsRecordFree()
    {
        const string mirroredEntitySource = """
            namespace KeyClassEntities;

            public class OrderLine
            {
                public virtual int OrderID { get; set; }

                public virtual int LineNo { get; set; }

                public virtual int Quantity { get; set; }
            }
            """;

        const string mirroredMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="KeyClassEntities">
                <class name="OrderLine" table="OrderLines">
                    <composite-id class="OrderLineId">
                        <key-property name="OrderID" column="OrderID" type="int" />
                        <key-property name="LineNo" column="LineNo" type="int" />
                    </composite-id>
                    <property name="Quantity" column="Quantity" type="int" />
                </class>
            </hibernate-mapping>
            """;

        var builder = ParseWith(mirroredMapping, mirroredEntitySource);
        var outputs = builder.Build();

        // The Mirrored form only duplicates parts the entity declares itself, so nothing
        // stands on the declarations of the class nobody declares: the completeness gate
        // lets the entity through, and the record that would explain a refusal has no
        // refusal to explain (decision 031, architecture.md 4.2).
        Assert.NotEmpty(outputs);
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("OrderLineId"));

        var entityMap = Assert.Single(builder.EntityMaps);
        Assert.Equal(
            new[] { "OrderID", "LineNo" },
            entityMap.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
    }
}
