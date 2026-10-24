using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// What a construct of excluded area 2 maps leaves the model (decision 177): the property with
/// one loss and nothing else said about it, a value type with the properties typed by it, and
/// no target writes a member the source maps somewhere the model does not reach - not as a
/// column of the entity's table, not as a member the framework leaves unpersisted, not as a
/// relation. One class for the three readers, because the rule is one rule.
/// </summary>
public class ExcludedConstructPropertyTest
{
    private static ConversionSource Source(ConversionContentType type, string content, string name) => new()
    {
        Name = name,
        ContentType = type,
        Content = content,
    };

    private static string Entity(ConversionResult result, string name)
        => Assert.Single(result.Sources, s => s.Entity == name && s.ContentType is ConversionContentType.CSharpEntity or ConversionContentType.JavaEntity).Content;

    private const string Client = """
        package shop;

        import jakarta.persistence.*;
        import java.util.Map;
        import java.util.Set;

        @Entity
        @Table(name = "CLIENT")
        public class Client {
            @Id
            @Column(name = "ID")
            private String id;

            @ElementCollection
            @MapKeyColumn(name = "NAME")
            @Column(name = "VALUE")
            @CollectionTable(name = "CLIENT_ATTRIBUTES", joinColumns = @JoinColumn(name = "CLIENT_ID"))
            private Map<String, String> attributes;

            @ElementCollection
            @Column(name = "VALUE")
            @CollectionTable(name = "WEB_ORIGINS", joinColumns = @JoinColumn(name = "CLIENT_ID"))
            private Set<String> webOrigins;

            @Embedded
            private Address address;

            private Address billing;
        }

        @Embeddable
        public class Address {
            private String street;
        }
        """;

    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AnElementCollectionAndAnEmbeddedClassLeaveEveryTarget(ORMEnum target)
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, target, [Source(ConversionContentType.Java, Client, "Client.java")]);

        // The embeddable class is no entity, so only the client comes out, and it carries none
        // of the four members: not the column VALUE twice, not @Transient, not a relation.
        Assert.DoesNotContain(result.Sources, s => s.Entity == "Address");
        var client = string.Concat(result.Sources.Where(s => s.Entity == "Client").Select(s => s.Content));
        foreach (var member in new[] { "attributes", "webOrigins", "address", "billing", "VALUE", "Transient", "Address" })
        {
            Assert.DoesNotContain(member, client, StringComparison.Ordinal);
        }

        // One loss per property, nothing about the annotations under the construct, nothing
        // about the unknown type Map<String, String> (decision 075).
        foreach (var (property, construct) in new[] { ("attributes", "@ElementCollection"), ("webOrigins", "@ElementCollection"), ("address", "@Embedded") })
        {
            var record = Assert.Single(result.Records, r => r.Property == property);
            Assert.Equal(ConversionRecordKind.Loss, record.Kind);
            Assert.Contains($"mapped by {construct}", record.Reason, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("@CollectionTable", StringComparison.Ordinal)
            || r.Reason.Contains("@MapKeyColumn", StringComparison.Ordinal));

        // The attribute JPA embeds without saying so is typed by the value type and leaves
        // with it, and the class says why it is gone.
        var billing = Assert.Single(result.Records, r => r.Property == "billing");
        Assert.Equal(ConversionRecordKind.Loss, billing.Kind);
        Assert.Contains("value type", billing.Reason, StringComparison.Ordinal);
        Assert.Single(result.Records, r => r.Entity == "Address" && r.Property is null && r.Reason.Contains("@Embeddable", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AnEmbeddableKeyClassIsAKeyAndNotAValueType()
    {
        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore,
        [
            Source(ConversionContentType.Java, """
                package shop;

                import jakarta.persistence.*;

                @Entity
                public class OrderLine {
                    @EmbeddedId
                    private OrderLineId id;

                    private Integer quantity;
                }

                @Embeddable
                public class OrderLineId {
                    private Integer orderId;
                    private Integer lineNo;
                }
                """, "OrderLine.java"),
        ]);

        var line = Entity(result, "OrderLine");
        Assert.Contains("[PrimaryKey(nameof(orderId), nameof(lineNo))]", line, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("decision 177", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheOrmXmlSpellingsLeaveTheModelInEitherOrderOfUnits(bool descriptorFirst)
    {
        var descriptor = Source(ConversionContentType.XML, """
            <?xml version="1.0" encoding="UTF-8"?>
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <entity class="shop.Client">
                    <attributes>
                        <id name="id"/>
                        <element-collection name="webOrigins"/>
                        <embedded name="address"/>
                    </attributes>
                </entity>
                <embeddable class="shop.Address"/>
            </entity-mappings>
            """, "orm.xml");
        var classes = Source(ConversionContentType.Java, """
            package shop;

            import java.util.Set;

            public class Client {
                private String id;
                private Set<String> webOrigins;
                private Address address;
            }

            public class Address {
                private String street;
            }
            """, "Client.java");

        var result = ConversionHandler.Convert(ORMEnum.Hibernate, ORMEnum.EFCore, descriptorFirst ? [descriptor, classes] : [classes, descriptor]);

        Assert.DoesNotContain(result.Sources, s => s.Entity == "Address");
        var client = Entity(result, "Client");
        Assert.DoesNotContain("webOrigins", client, StringComparison.Ordinal);
        Assert.DoesNotContain("address", client, StringComparison.Ordinal);
        Assert.Contains(result.Records, r => r.Property == "webOrigins" && r.Reason.Contains("<element-collection name=\"webOrigins\">", StringComparison.Ordinal));
        Assert.Contains(result.Records, r => r.Property == "address" && r.Reason.Contains("<embedded name=\"address\">", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.Hibernate)]
    public void AnOwnedTypeAndAComplexTypeAreNoEntitiesAndTheirHoldersNoRelations(ORMEnum target)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target,
        [
            Source(ConversionContentType.CSharp, """
                using System.Collections.Generic;
                using System.ComponentModel.DataAnnotations;
                using Microsoft.EntityFrameworkCore;

                namespace Shop;

                public class Customer
                {
                    [Key]
                    public int Id { get; set; }

                    public Address Address { get; set; } = new();

                    public List<Address> Previous { get; set; } = new();

                    public Money Credit { get; set; } = new();
                }

                [Owned]
                public class Address
                {
                    public string Street { get; set; } = "";
                }

                [ComplexType]
                public class Money
                {
                    public decimal Amount { get; set; }
                }
                """, "Customer.cs"),
        ]);

        Assert.Equal(["Customer"], result.Sources.Select(s => s.Entity).Distinct());
        var customer = string.Concat(result.Sources.Select(s => s.Content));
        Assert.DoesNotContain("Address", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("Money", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("many-to-one", customer, StringComparison.Ordinal);

        foreach (var property in new[] { "Address", "Previous", "Credit" })
        {
            Assert.Equal(ConversionRecordKind.Loss, Assert.Single(result.Records, r => r.Entity == "Customer" && r.Property == property).Kind);
        }

        Assert.Single(result.Records, r => r.Entity == "Address" && r.Reason.Contains("[Owned]", StringComparison.Ordinal));
        Assert.Single(result.Records, r => r.Entity == "Money" && r.Reason.Contains("[ComplexType]", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    private const string ProductClasses = """
        namespace Shop;

        public class Product
        {
            public virtual int Id { get; set; }
            public virtual string Name { get; set; }
            public virtual Address Address { get; set; }
            public virtual string Extra { get; set; }
            public virtual ISet<string> Tags { get; set; }
        }

        public class Address
        {
            public virtual string Street { get; set; }
        }
        """;

    private const string ProductMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop">
            <class name="Product" table="Products">
                <id name="Id" type="Int32">
                    <generator class="native" />
                </id>
                <property name="Name" />
                <component name="Address" class="Address">
                    <property name="Street" />
                </component>
                <set name="Tags" table="ProductTags">
                    <key column="ProductId" />
                    <element column="Tag" type="String" />
                </set>
                <join table="ProductExtras">
                    <key column="Id" />
                    <property name="Extra" />
                </join>
            </class>
        </hibernate-mapping>
        """;

    [Theory]
    [InlineData(ORMEnum.NHibernate, true)]
    [InlineData(ORMEnum.NHibernate, false)]
    [InlineData(ORMEnum.EFCore, true)]
    public void AComponentAValueCollectionAndAJoinedColumnLeaveTheModel(ORMEnum target, bool mappingFirst)
    {
        var classes = Source(ConversionContentType.CSharp, ProductClasses, "Product.cs");
        var mapping = Source(ConversionContentType.XML, ProductMapping, "Product.hbm.xml");

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, target, mappingFirst ? [mapping, classes] : [classes, mapping]);

        // The component's class is no entity, and the class of the entity keeps none of the
        // three members, which the targets used to write as columns of Products (decision 177).
        Assert.Equal(["Product"], result.Sources.Select(s => s.Entity).Distinct());
        var product = string.Concat(result.Sources.Select(s => s.Content));
        Assert.Contains("Name", product, StringComparison.Ordinal);
        foreach (var member in new[] { "Address", "Extra", "Tags" })
        {
            Assert.DoesNotContain(member, product, StringComparison.Ordinal);
            Assert.Equal(ConversionRecordKind.Loss, Assert.Single(result.Records, r => r.Entity == "Product" && r.Property == member).Kind);
        }

        Assert.Single(result.Records, r => r.Entity == "Address" && r.Property is null && r.Reason.Contains("as a value", StringComparison.Ordinal));
    }
}
