using DapperWrappers;
using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The neutral language type model (decision 014): an unrecognized name survives the
/// round trip instead of throwing, and a mapping's claim turns a navigation property
/// into a reference the target builder can emit.
/// </summary>
public class LangTypeTest
{
    [Fact]
    public void UnknownTypeSurvivesParseAndBuildUnderItsOwnName()
    {
        var source = """
            public class OrderLine
            {
                public int OrderID { get; set; }

                public uint Quantity { get; set; }

                public OrderLineId Id { get; set; }
            }
            """;

        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(source);

        var quantity = builder.EntityMap.Entity.Properties.Single(p => p.Name == "Quantity");
        Assert.Equal(LangTypeCategory.Unknown, quantity.Type!.Category);
        Assert.Equal("uint", quantity.Type.SourceName);

        // The name the source wrote is written back: an unknown type is an incomplete
        // claim, not an incomplete artifact.
        var code = builder.Build().Single().Content;
        Assert.Contains("public uint Quantity { get; set; }", code);
        Assert.Contains("public OrderLineId Id { get; set; }", code);
    }

    [Fact]
    public void ManyToOneMappingTurnsTheNavigationIntoAReference()
    {
        var entitySource = """
            namespace Sample;

            public class Order
            {
                public virtual int OrderID { get; set; }

                public virtual Customer Customer { get; set; }
            }
            """;

        var xmlMapping = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Sample">
                <class name="Sample.Order, Sample" table="Orders">
                    <id name="OrderID" column="OrderID" type="Int32">
                        <generator class="identity" />
                    </id>
                    <many-to-one name="Customer" class="Customer" />
                </class>
            </hibernate-mapping>
            """;

        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse(entitySource);
        new NHibernateXMLMappingParser(builder).Parse(xmlMapping);

        // The C# side alone reads Customer as an unknown name; the mapping's
        // <many-to-one> is the claim that makes it a reference.
        var navigation = builder.EntityMap.Entity.Properties.Single(p => p.Name == "Customer");
        Assert.Equal(LangTypeCategory.Reference, navigation.Type!.Category);
        Assert.Equal("Customer", navigation.Type.TargetEntity);

        // Generating C# for the navigation no longer falls over the entity type.
        var code = builder.Build().First(o => o.ContentType == Model.ConversionContentType.CSharpEntity).Content;
        Assert.Contains("public virtual Customer Customer { get; set; }", code);
    }

    [Fact]
    public void CollectionKeepsItsKindAndGainsAReferenceElement()
    {
        var source = """
            public class Customer
            {
                [Key]
                public int CustomerID { get; set; }

                public HashSet<Order> Orders { get; set; } = [];
            }
            """;

        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(source);

        var orders = builder.EntityMap.Entity.Properties.Single(p => p.Name == "Orders");
        Assert.Equal(LangTypeCategory.Collection, orders.Type!.Category);
        Assert.Equal(CollectionKind.Set, orders.Type.CollectionKind);
        Assert.Equal(LangTypeCategory.Reference, orders.Type.ElementType!.Category);
        Assert.Equal("Order", orders.Type.ElementType.TargetEntity);

        var code = builder.Build().Single().Content;
        Assert.Contains("public HashSet<Order> Orders { get; set; } = [];", code);
    }
}
