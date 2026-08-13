using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// NHibernate writes a composite key in three shapes: key properties on the entity, the same with
/// a key class mirroring them, and a key class the entity holds in a single property. The parser
/// has to tell them apart, because every target renders the key flat (decision 006) and the class
/// then survives only as a record next to the key.
/// </summary>
public class NHibernateCompositeIdTest
{
    private const string EntityClass = """
        public class OrderLine
        {
            public virtual int OrderID { get; set; }

            public virtual int LineNumber { get; set; }
        }
        """;

    private static string Mapping(string compositeId) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="OrderLine" table="OrderLines">
        {compositeId}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder Parse(string compositeId)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(EntityClass);
        new NHibernateXMLMappingParser(builder).Parse(Mapping(compositeId));
        return builder;
    }

    private static NHibernateEntityBuilder ParseMappingOnly(string compositeId)
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse(Mapping(compositeId));
        return builder;
    }

    [Fact]
    public void ClassAttributeAloneMeansTheKeyClassMirrorsTheEntity()
    {
        var builder = Parse("""
                <composite-id class="OrderLineId">
                    <key-property name="OrderID" column="OrderId" type="Int32" />
                    <key-property name="LineNumber" column="LineNo" type="Int32" />
                </composite-id>
        """);

        var pk = builder.EntityMap.PrimaryKey;
        Assert.NotNull(pk);

        // In this shape the key properties belong to the entity and the class only duplicates
        // them, so the key is read exactly as it would be without the attribute.
        Assert.Equal(new[] { "OrderID", "LineNumber" }, pk.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Equal(new[] { "OrderId", "LineNo" }, pk.Parts.Select(p => p.PropertyMap.ColumnName));

        var keyClass = pk.SourceKeyClass;
        Assert.NotNull(keyClass);
        Assert.Equal("OrderLineId", keyClass.ClassName);
        Assert.Equal(KeyClassForm.Mirrored, keyClass.Form);
        Assert.Null(keyClass.PropertyName);
    }

    [Fact]
    public void NameWithClassMeansTheEntityHoldsTheKeyClassInAProperty()
    {
        // Only the mapping is parsed here. The entity of this shape carries a property typed by the
        // key class, and the type model has no value for such a type, so parsing its C# would throw
        // before anything about the key could be read - that is the next step's problem.
        var builder = ParseMappingOnly("""
                <composite-id name="Id" class="OrderLineId">
                    <key-property name="OrderID" column="OrderId" type="Int32" />
                    <key-property name="LineNumber" column="LineNo" type="Int32" />
                </composite-id>
        """);

        var keyClass = builder.EntityMap.PrimaryKey!.SourceKeyClass;
        Assert.NotNull(keyClass);
        Assert.Equal("OrderLineId", keyClass.ClassName);
        Assert.Equal(KeyClassForm.Embedded, keyClass.Form);
        Assert.Equal("Id", keyClass.PropertyName);
    }

    [Fact]
    public void CompositeIdWithoutAKeyClassCarriesNoSignal()
    {
        var builder = Parse("""
                <composite-id>
                    <key-property name="OrderID" column="OrderId" type="Int32" />
                    <key-property name="LineNumber" column="LineNo" type="Int32" />
                </composite-id>
        """);

        // Absence keeps its meaning: the source expressed the key without a class at all.
        Assert.Null(builder.EntityMap.PrimaryKey!.SourceKeyClass);
    }

    [Fact]
    public void KeyClassDoesNotReachTheGeneratedMapping()
    {
        var builder = Parse("""
                <composite-id class="OrderLineId">
                    <key-property name="OrderID" column="OrderId" type="Int32" />
                    <key-property name="LineNumber" column="LineNo" type="Int32" />
                </composite-id>
        """);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // Reading the class is not the same as writing it back: the key is emitted as its parts and
        // the class name disappears, which is what flat rendering means (decision 006).
        Assert.Contains("<composite-id>", xml);
        Assert.Contains("<key-property name=\"OrderID\" column=\"OrderId\"", xml);
        Assert.DoesNotContain("OrderLineId", xml);
    }
}