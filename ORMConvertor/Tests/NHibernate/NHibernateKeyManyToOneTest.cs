using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.NHibernate;

/// <summary>
/// A &lt;key-many-to-one&gt; is a key part that is at the same time a reference to another
/// entity. The flat key (decision 006) has no reference-typed parts, so it is read as its
/// columns - scalar key parts whose language types arrive from the referenced key when the
/// pairs resolve - plus an owning many-to-one relation; the changed form is reported.
/// Before this, the part vanished without a trace and the key came out shorter than the
/// source described.
/// </summary>
public class NHibernateKeyManyToOneTest
{
    private const string OrderSource = """
        namespace NHibernateEntities;

        public class Order
        {
            public virtual int OrderId { get; set; }

            public virtual string Code { get; set; } = string.Empty;
        }
        """;

    private const string OrderMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Order, NHibernateEntities" table="Orders">
                <id name="OrderId" column="OrderId" type="int">
                    <generator class="identity" />
                </id>
                <property name="Code" not-null="true" length="20" />
            </class>
        </hibernate-mapping>
        """;

    private const string OrderLineSource = """
        namespace NHibernateEntities;

        public class OrderLine
        {
            public virtual Order Order { get; set; } = null!;

            public virtual int LineNo { get; set; }

            public virtual string Description { get; set; } = string.Empty;
        }
        """;

    private const string OrderLineMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.OrderLine, NHibernateEntities" table="OrderLines">
                <composite-id>
                    <key-many-to-one name="Order" class="Order" column="OrderId" />
                    <key-property name="LineNo" type="int" />
                </composite-id>
                <property name="Description" not-null="true" length="100" />
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder Parse(params (string? Entity, string Mapping)[] sources)
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);

        foreach (var (entity, _) in sources)
        {
            if (entity is not null)
            {
                entityParser.Parse(entity);
            }
        }

        foreach (var (_, mapping) in sources)
        {
            mappingParser.Parse(mapping);
        }

        return builder;
    }

    private static NHibernateEntityBuilder ParseDefault()
        => Parse((OrderSource, OrderMapping), (OrderLineSource, OrderLineMapping));

    [Fact]
    public void ColumnsBecomeKeyPartsInDocumentOrderAndTheReferenceARelation()
    {
        var builder = ParseDefault();

        var orderLine = builder.EntityMaps.Single(em => em.Entity.Name == "OrderLine");

        Assert.Equal(["OrderId", "LineNo"],
            orderLine.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.All(orderLine.PrimaryKey.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));

        var relation = Assert.Single(orderLine.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Order", relation.TargetEntity);
        Assert.Equal("Order", relation.SourceNavigationProperty);

        // The changed form carries a record: the reference form of the key part is not
        // restated in the output (decision 010).
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "Order");
    }

    [Fact]
    public void TheColumnPartTakesItsTypeFromTheReferencedKey()
    {
        var builder = ParseDefault();
        builder.Build();

        var orderLine = builder.EntityMaps.Single(em => em.Entity.Name == "OrderLine");
        var orderId = orderLine.PropertyMaps.Single(pm => pm.Property.Name == "OrderId");

        // The class holds only the navigation; the scalar part the flat key needs is typed
        // from the key part its column references, and the derivation is reported.
        Assert.Equal(ScalarType.Int, orderId.Property.Type?.ScalarType);
        Assert.Equal(DatabaseType.Integer, orderId.Type);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Property == "OrderId" && r.Reason.Contains("taken over"));

        var pair = Assert.Single(Assert.Single(orderLine.Relations).ColumnPairs);
        Assert.Equal("OrderId", pair.Source.Property.Name);
        Assert.Equal("OrderId", pair.Target.Property.Name);
    }

    [Fact]
    public void TheMappingRendersFlatWithAReadOnlyReference()
    {
        var builder = ParseDefault();
        var outputs = builder.Build();

        var xml = outputs.Where(o => o.ContentType == ConversionContentType.XML)
            .Single(o => o.Content.Contains("table=\"OrderLines\""));

        Assert.Contains("<key-property name=\"OrderId\"", xml.Content);
        Assert.Contains("<key-property name=\"LineNo\"", xml.Content);
        Assert.DoesNotContain("<key-many-to-one", xml.Content);

        // The relation's column is a key column, so the identifier keeps the write and the
        // reference is mapped read-only - otherwise NHibernate refuses the repeated column.
        Assert.Contains(
            "<many-to-one name=\"Order\" class=\"Order\" column=\"OrderId\" insert=\"false\" update=\"false\" />",
            xml.Content);
    }

    [Fact]
    public void AMultiColumnReferenceContributesOnePartPerColumn()
    {
        const string orderSource = """
            namespace NHibernateEntities;

            public class Order
            {
                public virtual int CompanyId { get; set; }

                public virtual int OrderId { get; set; }
            }
            """;

        const string orderMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.Order, NHibernateEntities" table="Orders">
                    <composite-id>
                        <key-property name="CompanyId" type="int" />
                        <key-property name="OrderId" type="int" />
                    </composite-id>
                </class>
            </hibernate-mapping>
            """;

        const string orderLineMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.OrderLine, NHibernateEntities" table="OrderLines">
                    <composite-id>
                        <key-many-to-one name="Order" class="Order">
                            <column name="CompanyId" />
                            <column name="OrderId" />
                        </key-many-to-one>
                        <key-property name="LineNo" type="int" />
                    </composite-id>
                </class>
            </hibernate-mapping>
            """;

        var builder = Parse((orderSource, orderMapping), (OrderLineSource, orderLineMapping));
        builder.Build();

        var orderLine = builder.EntityMaps.Single(em => em.Entity.Name == "OrderLine");

        Assert.Equal(["CompanyId", "OrderId", "LineNo"],
            orderLine.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));

        var relation = Assert.Single(orderLine.Relations);
        Assert.Equal(2, relation.ColumnPairs.Count);
        Assert.All(orderLine.PrimaryKey.Parts, p => Assert.NotNull(p.PropertyMap.Property.Type));
    }

    [Fact]
    public void WithoutStatedColumnsThePartIsReportedNotSilentlyDropped()
    {
        const string orderLineMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.OrderLine, NHibernateEntities" table="OrderLines">
                    <composite-id>
                        <key-many-to-one name="Order" class="Order" />
                        <key-property name="LineNo" type="int" />
                    </composite-id>
                </class>
            </hibernate-mapping>
            """;

        var builder = Parse((OrderSource, OrderMapping), (OrderLineSource, orderLineMapping));

        var orderLine = builder.EntityMaps.Single(em => em.Entity.Name == "OrderLine");

        // The key still comes out shorter - there is no scalar part to stand in for the
        // reference - but no longer without a trace, and the reference itself survives as
        // an ordinary relation.
        Assert.Equal(["LineNo"],
            orderLine.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.Single(orderLine.Relations);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Property == "Order"
            && r.Reason.Contains("fewer parts"));
    }
}
