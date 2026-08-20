using AbstractWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over a composite key expressed by a
/// key class in the Embedded form: the dissolution phase of decision 031 moves the class's
/// declarations onto the key parts, the entity comes out flat, and both target frameworks
/// accept it - which is exactly the input that used to end at the completeness gate.
/// </summary>
public class EmbeddedKeyClassVerificationTest
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
            <class name="KeyClassEntities.OrderLine, KeyClassEntities" table="OrderLines">
                <composite-id name="Id" class="OrderLineId">
                    <key-property name="OrderID" column="OrderID" type="int" />
                    <key-property name="LineNo" column="LineNo" type="int" />
                </composite-id>
                <property name="Quantity" column="Quantity" type="int" />
            </class>
        </hibernate-mapping>
        """;

    private static List<ConversionSource> Convert(AbstractEntityBuilder builder)
    {
        var entityParser = new NHibernateEntityParser(builder);
        entityParser.Parse(EntitySource);
        entityParser.Parse(KeyClassSource);
        new NHibernateXMLMappingParser(builder).Parse(EmbeddedMapping);
        return builder.Build();
    }

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            "KeyClassEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheDissolvedKeyClass()
    {
        var outputs = Convert(new NHibernateEntityBuilder());

        var mapping = Assert.Single(outputs, o => o.ContentType == ConversionContentType.XML);
        var errors = NHibernateMappingSchema.Validate(mapping.Content);
        Assert.True(errors.Count == 0, "Generated mapping is invalid:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));

        // Completing without an exception is the verdict: NHibernate bound the flat
        // composite key whose parts nobody declared on the entity class itself - they
        // came from the key class the mapping named (decision 031).
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            [mapping.Content]);
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelFromTheDissolvedKeyClass()
    {
        var outputs = Convert(new EFCoreEntityBuilder());

        var model = EFCoreAcceptance.BuildModel(
            CompileEntities(outputs, GeneratedEntityCompiler.EFCoreConsumerReferences));

        var orderLine = model.FindEntityType("KeyClassEntities.OrderLine");
        Assert.NotNull(orderLine);
        Assert.Equal(["OrderID", "LineNo"],
            orderLine.FindPrimaryKey()!.Properties.Select(p => p.Name));

        // The key class did not become an entity of the model (rule E1).
        Assert.Null(model.FindEntityType("KeyClassEntities.OrderLineId"));
    }
}
