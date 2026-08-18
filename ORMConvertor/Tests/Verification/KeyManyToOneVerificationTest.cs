using AbstractWrappers;
using EFCoreWrappers;
using Microsoft.EntityFrameworkCore;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over a source key part written as
/// &lt;key-many-to-one&gt;: the flat reading - scalar key parts typed from the referenced
/// key, plus a read-only reference - compiles and both target frameworks accept it. The
/// source states everything, so the run is dry.
/// </summary>
public class KeyManyToOneVerificationTest
{
    private const string OrderSource = """
        namespace KeyPartEntities;

        public class Order
        {
            public virtual int OrderId { get; set; }

            public virtual string Code { get; set; } = string.Empty;
        }
        """;

    private const string OrderMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="KeyPartEntities">
            <class name="KeyPartEntities.Order, KeyPartEntities" table="Orders">
                <id name="OrderId" column="OrderId" type="int">
                    <generator class="identity" />
                </id>
                <property name="Code" not-null="true" length="20" />
            </class>
        </hibernate-mapping>
        """;

    private const string OrderLineSource = """
        namespace KeyPartEntities;

        public class OrderLine
        {
            public virtual Order Order { get; set; } = null!;

            public virtual int LineNo { get; set; }

            public virtual string Description { get; set; } = string.Empty;
        }
        """;

    private const string OrderLineMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="KeyPartEntities">
            <class name="KeyPartEntities.OrderLine, KeyPartEntities" table="OrderLines">
                <composite-id>
                    <key-many-to-one name="Order" class="Order" column="OrderId" />
                    <key-property name="LineNo" type="int" />
                </composite-id>
                <property name="Description" not-null="true" length="100" />
            </class>
        </hibernate-mapping>
        """;

    private static List<ConversionSource> Convert(AbstractEntityBuilder builder)
    {
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(OrderSource);
        entityParser.Parse(OrderLineSource);
        mappingParser.Parse(OrderMapping);
        mappingParser.Parse(OrderLineMapping);
        return builder.Build();
    }

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            "KeyPartEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheFlatReading()
    {
        var outputs = Convert(new NHibernateEntityBuilder());

        var mappings = outputs.Where(o => o.ContentType == ConversionContentType.XML).ToList();
        Assert.Equal(2, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });

        // Completing without an exception is the verdict: NHibernate bound the flat
        // composite key - including the part nobody declared in the class - the identity
        // members and the read-only reference over the key column.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            mappings.Select(m => m.Content));
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelFromTheFlatReading()
    {
        var outputs = Convert(new EFCoreEntityBuilder());

        var model = EFCoreAcceptance.BuildModel(
            CompileEntities(outputs, GeneratedEntityCompiler.EFCoreConsumerReferences));

        var orderLine = model.FindEntityType("KeyPartEntities.OrderLine");
        Assert.NotNull(orderLine);
        Assert.Equal(["OrderId", "LineNo"],
            orderLine.FindPrimaryKey()!.Properties.Select(p => p.Name));

        // The reference part becomes an identifying foreign key: its property is at the
        // same time the leading part of the key.
        var foreignKey = Assert.Single(orderLine.GetForeignKeys());
        Assert.Equal("KeyPartEntities.Order", foreignKey.PrincipalEntityType.Name);
        Assert.Equal(["OrderId"], foreignKey.Properties.Select(p => p.Name));
    }
}
