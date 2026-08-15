using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The resolution phase promised by decision 001: before generation, relation targets resolve
/// by name against the entities of the same conversion, and the foreign key columns the source
/// stated pair up with the key they reference (decision 012). A target outside the conversion
/// is the database catalog's case and everything stays as parsed.
/// </summary>
public class EntityNameResolutionTest
{
    private const string CustomerCs = """
        public class Customer
        {
            public virtual int CustomerID { get; set; }
        }
        """;

    private const string CustomerXml = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers">
                <id name="CustomerID" column="CustomerID" type="Int32">
                    <generator class="identity" />
                </id>
                <bag name="Orders" inverse="true" cascade="all-delete-orphan">
                    <key column="CustomerRef" />
                    <one-to-many class="Order" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    private const string OrderCs = """
        public class Order
        {
            public virtual int OrderID { get; set; }

            public virtual Customer Customer { get; set; }
        }
        """;

    private const string OrderXml = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Order" table="Orders">
                <id name="OrderID" column="OrderID" type="Int32">
                    <generator class="identity" />
                </id>
                <many-to-one name="Customer" class="Customer" column="CustomerRef" />
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder ParseBoth()
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var xmlParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(CustomerCs);
        entityParser.Parse(OrderCs);
        xmlParser.Parse(CustomerXml);
        xmlParser.Parse(OrderXml);

        return builder;
    }

    [Fact]
    public void StatedForeignKeyColumnPairsWithTheTargetsKey()
    {
        var builder = ParseBoth();
        var outputs = builder.Build();

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var relation = order.Relations.Single();

        // One pair: the stated column against the single part of Customer's key. The target
        // side is the very property map of the key, not a copy.
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustomerRef", pair.Source.ColumnName);
        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.Same(customer.PrimaryKey!.Parts[0].PropertyMap, pair.Target);

        // The column used to be dropped on reading; now it survives into the output.
        var orderXml = outputs.Single(o =>
            o.ContentType == ConversionContentType.XML && o.Content.Contains("<class name=\"Order\""));
        Assert.Contains("<many-to-one name=\"Customer\" class=\"Customer\" column=\"CustomerRef\" />", orderXml.Content);
    }

    [Fact]
    public void CollectionKeyColumnBelongsToTheChildAndSurvives()
    {
        var builder = ParseBoth();
        var outputs = builder.Build();

        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        var relation = customer.Relations.Single();

        // The stated column is the child's, so it sits on the foreign key side of the pair;
        // the referenced key is the parent's own.
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustomerRef", pair.Source.ColumnName);
        Assert.Same(customer.PrimaryKey!.Parts[0].PropertyMap, pair.Target);

        // Before the pairing the builder fell back to the owner's key column (CustomerID),
        // which was the wrong table's column whenever the names differed.
        var customerXml = outputs.Single(o =>
            o.ContentType == ConversionContentType.XML && o.Content.Contains("<class name=\"Customer\""));
        Assert.Contains("<key column=\"CustomerRef\" />", customerXml.Content);
    }

    [Fact]
    public void ForeignKeyAnnotationIsReadAndWrittenBack()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse("""
            public class Customer
            {
                [Key]
                public int CustomerID { get; set; }
            }
            """);
        parser.Parse("""
            public class Order
            {
                [Key]
                public int OrderID { get; set; }

                public int CustomerRef { get; set; }

                [ForeignKey("CustomerRef")]
                public Customer Customer { get; set; }
            }
            """);

        var outputs = builder.Build();

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var relation = order.Relations.Single();
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);

        // The annotation names an existing property, so the pair points at it and the output
        // restates the annotation instead of leaving the target to its own convention.
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustomerRef", pair.Source.Property.Name);

        var orderCode = outputs.Single(o => o.Content.Contains("class Order"));
        Assert.Contains("[ForeignKey(\"CustomerRef\")]", orderCode.Content);
    }

    [Fact]
    public void UnknownTypeNamingAnEntityOfTheConversionBecomesAReference()
    {
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse("""
            public class Customer
            {
                public int Id { get; set; }
            }

            public class Order
            {
                public Customer Customer { get; set; }

                public OtherThing Other { get; set; }
            }
            """);

        builder.Build();

        // Dapper states no relations, so the C# side alone carries the claim - and the name
        // resolves against the entities of the same conversion (decision 014, second half of
        // the Reference rule). A name that resolves to nothing stays unknown.
        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var navigation = order.Entity.Properties.Single(p => p.Name == "Customer");
        Assert.Equal(LangTypeCategory.Reference, navigation.Type!.Category);
        Assert.Equal("Customer", navigation.Type.TargetEntity);

        var other = order.Entity.Properties.Single(p => p.Name == "Other");
        Assert.Equal(LangTypeCategory.Unknown, other.Type!.Category);
    }

    [Fact]
    public void TargetOutsideTheConversionLeavesTheColumnsUnpaired()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(OrderCs);
        new NHibernateXMLMappingParser(builder).Parse(OrderXml);

        builder.Build();

        // Customer is not part of the conversion, so there is no key to pair with - filling
        // the pairs from anything but the source is the catalog's job (decision 015).
        var relation = builder.EntityMap.Relations.Single();
        Assert.Empty(relation.ColumnPairs);
    }
}
