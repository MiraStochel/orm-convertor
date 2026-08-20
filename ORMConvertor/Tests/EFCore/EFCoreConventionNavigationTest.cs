using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.EFCore;

/// <summary>
/// A reference navigation needs no [ForeignKey] in EF Core - the relationship follows by
/// convention from the type being an entity of the model. The parser claims the relation
/// for such a property too, deferred until the entities of the conversion are known, and
/// finds the foreign key property by EF Core's naming convention where the class has one;
/// where it has none, the relation goes without columns and the target says so instead of
/// staying silent.
/// </summary>
public class EFCoreConventionNavigationTest
{
    private const string CustomerSource = """
        public class Customer
        {
            [Key]
            public int CustomerID { get; set; }
        }
        """;

    private const string OrderSource = """
        public class Order
        {
            [Key]
            public int OrderID { get; set; }

            public int CustomerID { get; set; }

            public Customer Customer { get; set; }
        }
        """;

    [Fact]
    public void NavigationWithoutTheAnnotationBecomesAnOwningRelation()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        // Order first: the claim has to survive until Customer is known at all.
        parser.Parse(OrderSource);
        parser.Parse(CustomerSource);

        var outputs = builder.Build();

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var relation = Assert.Single(order.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Customer", relation.TargetEntity);

        // CustomerID matches EF Core's {TargetType}Id pattern case-insensitively and its
        // type matches the key part's, so the convention names it and the output makes the
        // pairing explicit.
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustomerID", pair.Source.Property.Name);

        var orderCode = outputs.Single(o => o.Content.Contains("class Order"));
        Assert.Contains("[ForeignKey(\"CustomerID\")]", orderCode.Content);
    }

    [Fact]
    public void WithoutAMatchingPropertyTheRelationGoesWithoutColumnsAndIsRecorded()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse("""
            public class Order
            {
                [Key]
                public int OrderID { get; set; }

                public Customer Customer { get; set; }
            }
            """);
        parser.Parse(CustomerSource);

        var outputs = builder.Build();

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var relation = Assert.Single(order.Relations);
        Assert.Empty(relation.ColumnPairs);

        // EF Core would fall back to a shadow property here - a column no class member
        // carries - which the model cannot state; the omission is a record, not silence.
        var orderCode = outputs.Single(o => o.Content.Contains("class Order"));
        Assert.DoesNotContain("[ForeignKey", orderCode.Content);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention
            && r.Category == MappingFactCategory.ForeignKeyColumns
            && r.Property == "Customer");
    }

    [Fact]
    public void ATypeNamingNoEntityOfTheConversionStaysAPlainProperty()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class OrderLine
            {
                [Key]
                public int OrderLineID { get; set; }

                public uint Quantity { get; set; }

                public OtherThing Other { get; set; }
            }
            """);

        var code = builder.Build().Single().Content;

        // uint is a scalar the vocabulary does not know and OtherThing resolves to no
        // entity of the conversion; neither is a navigation anyone stated, so no relation
        // arises and both survive under their own names, as unknown types always have.
        Assert.Empty(builder.EntityMaps.Single().Relations);
        Assert.Contains("public required uint Quantity { get; set; }", code);
        Assert.Contains("public required OtherThing Other { get; set; }", code);
    }

    [Fact]
    public void TheConventionTranslatesIntoTheNHibernateMapping()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(OrderSource);
        parser.Parse(CustomerSource);

        var outputs = builder.Build();

        // The navigation used to leave this conversion as <property name="Customer" />,
        // which NHibernate refuses because the type is not mapped as a value; now the
        // relation carries it as <many-to-one> with the convention-derived column, and the
        // scalar behind the same column goes read-only per the repeated-column rule.
        var orderXml = outputs.Single(o =>
            o.ContentType == Model.ConversionContentType.XML && o.Content.Contains("<class name=\"Order\""));
        Assert.Contains("<many-to-one name=\"Customer\" class=\"Customer\" column=\"CustomerID\" />", orderXml.Content);
        Assert.DoesNotContain("<property name=\"Customer\"", orderXml.Content);
    }
}
