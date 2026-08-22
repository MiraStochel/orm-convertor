using EFCoreWrappers;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

public class RelationModelTest
{
    [Fact]
    public void EFCoreCollectionIsParsedAsInverseRelationOnEntity()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(SampleData.CustomerSampleEFCore.Entity);

        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("Customer", relation.SourceEntity);
        Assert.Equal("CustomerTransaction", relation.TargetEntity);
        Assert.Equal("Transactions", relation.SourceNavigationProperty);
        Assert.Empty(relation.ColumnPairs);
    }

    [Fact]
    public void NHibernateManyToOneIsParsedAsOwningRelation()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="CustomerTransaction" table="CustomerTransactions" schema="Sales">
                    <id name="TransactionID" column="TransactionID" type="int">
                        <generator class="identity" />
                    </id>
                    <many-to-one name="Owner" class="Customer" column="CustomerID" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Customer", relation.TargetEntity);
        Assert.Equal("Owner", relation.SourceNavigationProperty);
    }

    [Fact]
    public void NHibernateOneToOneWithoutColumnsIsTheInverseSide()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers" schema="Sales">
                    <id name="CustomerID" column="CustomerID" type="int">
                        <generator class="identity" />
                    </id>
                    <one-to-one name="Profile" class="CustomerProfile" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        // <one-to-one> carries no column, so this side holds no foreign key: either the far side
        // holds it, or both entities share the primary key (decision 012).
        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
    }

    [Fact]
    public void NHibernateOneToOneWithConstrainedIsTheOwningSide()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="CustomerProfile" table="CustomerProfiles" schema="Sales">
                    <id name="CustomerID" column="CustomerID" type="int">
                        <generator class="foreign">
                            <param name="property">Customer</param>
                        </generator>
                    </id>
                    <one-to-one name="Customer" class="Customer" constrained="true" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        // constrained="true" says this entity takes its identity from the other one, so it is the
        // dependent side even though it has no foreign key column of its own.
        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);

        // The generator names the property the identity comes from, which is what makes the shared
        // key recognizable later without reaching into the other entity (decision 011). A strategy
        // on the escape path keeps its parameters there too (decision 020), so property stays
        // verbatim on the literal side, where SharesPrimaryKeyThrough reads it.
        var part = Assert.Single(builder.EntityMap.PrimaryKey!.Parts);
        Assert.Equal("foreign", part.SourceStrategyName);
        Assert.Equal("Customer", part.SourceStrategyParameters["property"]);
        Assert.Empty(part.StrategyParameters);
    }

    [Fact]
    public void NHibernateManyToOneWithUniqueIsAOneToOneRelation()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new NHibernateXMLMappingParser(builder);

        const string xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers" schema="Sales">
                    <id name="CustomerID" column="CustomerID" type="int">
                        <generator class="identity" />
                    </id>
                    <many-to-one name="Profile" class="CustomerProfile" column="ProfileID" unique="true" />
                </class>
            </hibernate-mapping>
            """;

        parser.Parse(xmlMapping);

        // The same element without unique="true" is N:1; the constraint is the whole difference,
        // and reading only the element name would degrade the relation on the way back.
        var relation = Assert.Single(builder.EntityMap.Relations);
        Assert.Equal(Cardinality.OneToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
    }

    [Fact]
    public void MultiColumnForeignKeyReferencesTargetKeyPartsInOrder()
    {
        var builder = new DummyEntityBuilder();

        // Target entity with a two-part key.
        builder.BeginEntity();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddProperty("int", "OrderID");
        builder.AddProperty("long", "CompanyID");
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Assigned),
            ("CompanyID", 2, PrimaryKeyStrategy.Assigned),
        ]);
        var target = builder.EntityMap;

        // Source entity referencing it through two foreign key columns.
        builder.BeginEntity();
        builder.AddClassHeader("public", "OrderLineAllocation");
        builder.AddProperty("int", "OrderRef");
        builder.AddProperty("long", "CompanyRef");
        var source = builder.EntityMap;

        // No parser fills ColumnPairs today - resolving them needs several entities at
        // once - so the relation is assembled through the builder API. What this test
        // pins down is that the model can express a multi-column foreign key at all.
        builder.AddRelation(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "OrderLineAllocation",
            TargetEntity = "OrderLine",
            ColumnPairs =
            [
                new ColumnPair
                {
                    Source = source.PropertyMaps.Single(pm => pm.Property.Name == "OrderRef"),
                    Target = target.PrimaryKey!.Parts[0].PropertyMap,
                },
                new ColumnPair
                {
                    Source = source.PropertyMaps.Single(pm => pm.Property.Name == "CompanyRef"),
                    Target = target.PrimaryKey!.Parts[1].PropertyMap,
                },
            ],
        });

        Assert.Equal(2, builder.EntityMaps.Count);

        var relation = Assert.Single(source.Relations);
        Assert.Equal(2, relation.ColumnPairs.Count);
        Assert.Equal("OrderRef", relation.ColumnPairs[0].Source.Property.Name);
        Assert.Equal("CompanyRef", relation.ColumnPairs[1].Source.Property.Name);

        // The target side is not a copy - the pairs point at the very property mappings
        // that make up the target key, in the key's own order.
        Assert.Same(target.PrimaryKey!.Parts[0].PropertyMap, relation.ColumnPairs[0].Target);
        Assert.Same(target.PrimaryKey!.Parts[1].PropertyMap, relation.ColumnPairs[1].Target);
    }
}