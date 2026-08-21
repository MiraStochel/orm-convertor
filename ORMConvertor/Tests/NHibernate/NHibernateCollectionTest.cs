using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.NHibernate;

/// <summary>
/// Collections carry only what somebody stated. The element follows the collection kind of
/// decision 014 - &lt;set&gt; for Set, &lt;bag&gt; for everything else - and the kind travels both
/// ways: the C# declaration fills it on parsing and the XML element shape fills an empty
/// fact. The attributes are no longer invented: inverse="true" is derived from whether the
/// owning counterpart is part of the conversion, cascade is never written, and what the
/// model cannot keep from the source - a stated inverse or cascade, the index of a
/// &lt;list&gt;, the key type of a &lt;map&gt; - is reported by the parser (decision 010).
/// </summary>
public class NHibernateCollectionTest
{
    private const string CustomerSource = """
        public class Customer
        {
            public virtual int CustomerID { get; set; }
        }
        """;

    private const string OrderSource = """
        public class Order
        {
            public virtual int OrderID { get; set; }

            public virtual Customer Customer { get; set; }
        }
        """;

    private const string OrderMapping = """
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

    private static string CustomerMappingWithCollection(string collectionXml) => $"""
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers">
                <id name="CustomerID" column="CustomerID" type="Int32">
                    <generator class="identity" />
                </id>
                {collectionXml}
            </class>
        </hibernate-mapping>
        """;

    private static NHibernateEntityBuilder Parse(string customerMapping, bool withOrder = true)
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var xmlParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(CustomerSource);
        if (withOrder)
        {
            entityParser.Parse(OrderSource);
        }

        xmlParser.Parse(customerMapping);
        if (withOrder)
        {
            xmlParser.Parse(OrderMapping);
        }

        return builder;
    }

    private static string CustomerXmlOf(NHibernateEntityBuilder builder)
        => builder.Build()
            .Single(o => o.ContentType == ConversionContentType.XML && o.Content.Contains("<class name=\"Customer\""))
            .Content;

    [Fact]
    public void ASetElementKeepsItsKindThroughTheRoundTrip()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <set name="Orders">
                <key column="CustomerRef" />
                <one-to-many class="Order" />
            </set>
            """));

        var xml = CustomerXmlOf(builder);

        // The kind is semantic - a set excludes duplicates - so <set> in means <set> out,
        // and the mapping stays valid against NHibernate's own schema.
        Assert.Contains("<set name=\"Orders\" inverse=\"true\">", xml);
        Assert.Contains("</set>", xml);
        Assert.Empty(NHibernateMappingSchema.Validate(xml));

        var orders = builder.EntityMaps.Single(em => em.Entity.Name == "Customer")
            .PropertyMaps.Single(pm => pm.Property.Name == "Orders");
        Assert.Equal(CollectionKind.Set, orders.Property.Type?.CollectionKind);
    }

    [Fact]
    public void TheDeclaredTypeOutranksTheMappingElement()
    {
        // The entity text is the first level of source precedence (decision 017): a kind
        // the C# declaration carries is not overwritten by the artifact's <bag>, which
        // states nothing beyond the default anyway.
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Customer
            {
                public virtual int CustomerID { get; set; }

                public virtual HashSet<Order> Orders { get; set; } = [];
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse(CustomerMappingWithCollection("""
            <bag name="Orders">
                <key column="CustomerRef" />
                <one-to-many class="Order" />
            </bag>
            """));

        Assert.Contains("<set name=\"Orders\">", CustomerXmlOf(builder));
    }

    [Fact]
    public void TheInverseAttributeIsDerivedFromTheOwningCounterpart()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <bag name="Orders">
                <key column="CustomerRef" />
                <one-to-many class="Order" />
            </bag>
            """));

        var xml = CustomerXmlOf(builder);

        // Order maps the same foreign key through its <many-to-one>, so the write belongs
        // there and inverse="true" restates the model; cascade stays a claim nobody made.
        Assert.Contains("<bag name=\"Orders\" inverse=\"true\">", xml);
        Assert.DoesNotContain("cascade", xml);
    }

    [Fact]
    public void WithoutTheOwningSideInverseIsNotClaimed()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <bag name="Orders">
                <key column="CustomerRef" />
                <one-to-many class="Order" />
            </bag>
            """), withOrder: false);

        // Order is outside the conversion, so nobody else could write the key: with
        // inverse="true" the association would never persist. The stated key column still
        // goes out verbatim - it belongs to the child table and needs no pairing to be true.
        var xml = CustomerXmlOf(builder);
        Assert.Contains("<bag name=\"Orders\">", xml);
        Assert.DoesNotContain("inverse", xml);
        Assert.Contains("<key column=\"CustomerRef\" />", xml);
    }

    [Fact]
    public void StatedInverseAndCascadeAreReportedAsLosses()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <bag name="Orders" inverse="true" cascade="all-delete-orphan">
                <key column="CustomerRef" />
                <one-to-many class="Order" />
            </bag>
            """));

        // The model has nowhere to keep either value, so the parser is the only place that
        // still sees them and reports the drop (decision 010) - like property-ref.
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Orders" && r.Reason.Contains("inverse=\"true\""));
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Orders" && r.Reason.Contains("cascade=\"all-delete-orphan\""));
    }

    [Fact]
    public void AListElementKeepsTheKindAndReportsTheDroppedOrder()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <list name="Orders">
                <key column="CustomerRef" />
                <list-index column="SortOrder" />
                <one-to-many class="Order" />
            </list>
            """));

        // The index column has no home in the model, so the persistent order cannot
        // survive; the kind survives on the language side and the mapping renders <bag>,
        // NHibernate's shape for a list-typed property without an index.
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Orders" && r.Reason.Contains("index"));

        var orders = builder.EntityMaps.Single(em => em.Entity.Name == "Customer")
            .PropertyMaps.Single(pm => pm.Property.Name == "Orders");
        Assert.Equal(CollectionKind.List, orders.Property.Type?.CollectionKind);

        Assert.Contains("<bag name=\"Orders\" inverse=\"true\">", CustomerXmlOf(builder));
    }

    [Fact]
    public void AMapElementIsReadAsAPlainCollectionWithARecord()
    {
        var builder = Parse(CustomerMappingWithCollection("""
            <map name="Orders">
                <key column="CustomerRef" />
                <map-key column="OrderCode" type="String" />
                <one-to-many class="Order" />
            </map>
            """));

        // Maps stay out of the model's scope (decision 014): the collection is read as a
        // plain one and the dropped shape is on record instead of vanishing.
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Property == "Orders" && r.Reason.Contains("<map>"));
        Assert.Contains("<bag name=\"Orders\" inverse=\"true\">", CustomerXmlOf(builder));
    }

    [Fact]
    public void ACollectionWithoutARelationStaysUnmapped()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Customer
            {
                public virtual int CustomerID { get; set; }

                public virtual List<string> Tags { get; set; } = [];
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse(CustomerMappingWithCollection(string.Empty));

        var outputs = builder.Build();
        var xml = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;
        var code = outputs.Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;

        // A <property> for a collection would make NHibernate refuse the whole document; the
        // class keeps the member, the mapping leaves it out, and the gap is on record.
        Assert.DoesNotContain("Tags", xml);
        Assert.Contains("IList<string> Tags", code);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Property == "Tags");
    }

    [Fact]
    public void ASurvivingManyToManyWritesTheStatedJunctionFacts()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            public class Supplier
            {
                public virtual int SupplierId { get; set; }

                public virtual List<Product> Products { get; set; } = [];
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Supplier" table="Suppliers">
                    <id name="SupplierId" column="SupplierId" type="Int32">
                        <generator class="identity" />
                    </id>
                    <bag name="Products" table="ProductSuppliers" schema="Sales">
                        <key column="SupplierId" />
                        <many-to-many class="Product" column="ProductId" />
                    </bag>
                </class>
            </hibernate-mapping>
            """);

        // Product is outside the conversion, so no junction entity can stand (decision 005)
        // and the relation stays many-to-many. Everything the source stated about the
        // junction table still goes back out - without the table the mapping would be
        // invalid, not merely poorer - and the missing entity is already on record.
        var xml = builder.Build()
            .Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("<bag name=\"Products\" table=\"ProductSuppliers\" schema=\"Sales\">", xml);
        Assert.Contains("<key column=\"SupplierId\" />", xml);
        Assert.Contains("<many-to-many class=\"Product\" column=\"ProductId\" />", xml);
        Assert.Empty(NHibernateMappingSchema.Validate(xml));
    }
}
