using AbstractWrappers;
using EFCoreWrappers;
using Microsoft.EntityFrameworkCore;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the NHibernate → EF Core
/// conversion: the generated classes compile and EF Core builds a validated model from
/// them. The model is then asked back for the facts the source stated - table, schema, key
/// parts, foreign key - so the verdict is the framework's reading of the artifact, not ours.
/// </summary>
public class NHibernateToEFCoreVerificationTest
{
    private const string OrderSource = """
        namespace NHibernateEntities;

        public class Order
        {
            public virtual int OrderID { get; set; }

            public virtual DateTime OrderDate { get; set; }

            public virtual string? Comments { get; set; }

            public virtual List<OrderLine> OrderLines { get; set; } = [];
        }
        """;

    private const string OrderMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Order, NHibernateEntities" table="Orders" schema="Sales">
                <id name="OrderID" column="OrderID" type="int">
                    <generator class="identity" />
                </id>
                <property name="OrderDate" not-null="true" type="datetime" />
                <property name="Comments" not-null="false" />
                <bag name="OrderLines" inverse="true" cascade="all-delete-orphan">
                    <key column="OrderID" />
                    <one-to-many class="OrderLine" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    private const string OrderLineSource = """
        namespace NHibernateEntities;

        public class OrderLine
        {
            public virtual int OrderID { get; set; }

            public virtual int OrderLineID { get; set; }

            public virtual string Description { get; set; }

            public virtual Order? Order { get; set; }
        }
        """;

    private const string OrderLineMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.OrderLine, NHibernateEntities" table="OrderLines" schema="Sales">
                <composite-id>
                    <key-property name="OrderID" column="OrderID" type="int" />
                    <key-property name="OrderLineID" column="OrderLineID" type="int" />
                </composite-id>
                <property name="Description" not-null="true" length="100" />
                <many-to-one name="Order" class="Order" column="OrderID" />
            </class>
        </hibernate-mapping>
        """;

    private static List<ConversionSource> Convert()
    {
        AbstractEntityBuilder builder = new EFCoreEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(OrderSource);
        entityParser.Parse(OrderLineSource);
        mappingParser.Parse(OrderMapping);
        mappingParser.Parse(OrderLineMapping);
        return builder.Build();
    }

    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            "NHibernateEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences);

    [Fact]
    public void GeneratedEntitiesCompile()
    {
        CompileEntities(Convert());
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelFromTheArtifacts()
    {
        var model = EFCoreAcceptance.BuildModel(CompileEntities(Convert()));

        // The framework's own reading of the artifact: the facts the source stated have to
        // come back out of the finalized model, not just survive in the text.
        var orderLine = model.FindEntityType("NHibernateEntities.OrderLine");
        Assert.NotNull(orderLine);
        Assert.Equal("OrderLines", orderLine.GetTableName());
        Assert.Equal("Sales", orderLine.GetSchema());
        Assert.Equal(["OrderID", "OrderLineID"],
            orderLine.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var foreignKey = Assert.Single(orderLine.GetForeignKeys());
        Assert.Equal("NHibernateEntities.Order", foreignKey.PrincipalEntityType.Name);
        Assert.Equal(["OrderID"], foreignKey.Properties.Select(p => p.Name));
    }
}
