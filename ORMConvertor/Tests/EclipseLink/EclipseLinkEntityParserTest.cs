using AbstractWrappers.Diagnostics;
using EclipseLinkWrappers;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.EclipseLink;

/// <summary>
/// The reading half of decision 080. The annotation subset is the specification's and is
/// tested over Hibernate; what is asserted here is what reading an EclipseLink source is
/// on its own: that no default of this implementation is materialized although the profile
/// names them (decision 067), that its vendor annotations are dropped aloud because not one
/// of them has a place in the model, and that a lazy reference - the only fact no artifact
/// can show - is reported with the reason no other implementation has.
/// </summary>
public class EclipseLinkEntityParserTest
{
    private static (DummyEntityBuilder Builder, IReadOnlyCollection<EntityMap> Read) Parse(string java)
    {
        var builder = new DummyEntityBuilder();
        var read = new EclipseLinkEntityParser(builder, new JpaReadingContext()).Parse(java);
        return (builder, read);
    }

    private static EntityMap Single(string java) => Parse(java).Builder.EntityMaps.Single();

    private static PropertyMap Map(EntityMap map, string name) => map.PropertyMaps.Single(pm => pm.Property.Name == name);

    /// <summary>The standard annotations read the same for both implementations - one source, two targets.</summary>
    [Fact]
    public void TheStandardAnnotationsAreReadAsTheyAreForHibernate()
    {
        var map = Single("""
            package Shop;

            import jakarta.persistence.*;
            import java.math.BigDecimal;

            @Entity
            @Table(name = "Customers", schema = "Sales")
            public class Customer {
                @Id
                @GeneratedValue(strategy = GenerationType.IDENTITY)
                @Column(name = "CustomerId")
                private Integer CustomerId;

                @Column(name = "CustomerName", length = 200, nullable = false)
                private String CustomerName;

                @Column(name = "CreditLimit", precision = 18, scale = 2)
                private BigDecimal CreditLimit;
            }
            """);

        Assert.Equal("Shop", map.Entity.Namespace);
        Assert.Equal("Customers", map.Table);
        Assert.Equal("Sales", map.Schema);
        Assert.Equal(PrimaryKeyStrategy.Identity, map.PrimaryKey!.Parts.Single().Strategy);
        Assert.Equal(200, Map(map, "CustomerName").Length);
        Assert.False(Map(map, "CustomerName").IsNullable);
        Assert.Equal(18, Map(map, "CreditLimit").Precision);
    }

    /// <summary>
    /// Decision 067 from the sharper side: the profile knows that this implementation would
    /// call the table CUSTOMER and resolve AUTO to a counter table, and the parser
    /// materializes neither. An absent default is not a statement of the source, and the
    /// catalog is where the silence is filled (F6).
    /// </summary>
    [Fact]
    public void NoDefaultOfTheImplementationIsMaterializedWhileReading()
    {
        var map = Single("""
            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id
                @GeneratedValue
                private Integer CustomerId;

                private String CustomerName;
            }
            """);

        Assert.Null(map.Table);
        Assert.Null(Map(map, "CustomerName").ColumnName);
        Assert.Equal(PrimaryKeyStrategy.Auto, map.PrimaryKey!.Parts.Single().Strategy);
        Assert.Empty(map.PrimaryKey.Parts.Single().StrategyParameters);
    }

    /// <summary>
    /// The third hook of decision 076 is empty here, and the shared reading is the whole
    /// answer: an EclipseLink annotation has no counterpart in the model and is dropped
    /// with its name, exactly as a Hibernate annotation is on the way to EclipseLink.
    /// </summary>
    [Fact]
    public void VendorAnnotationsOfEclipseLinkAreDroppedAloud()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import org.eclipse.persistence.annotations.CacheIndex;
            import org.eclipse.persistence.annotations.PrivateOwned;

            @Entity
            public class Customer {
                @Id
                private Integer CustomerId;

                @CacheIndex
                private String Sku;

                @PrivateOwned
                private String Note;
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("CacheIndex"));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("PrivateOwned"));
    }

    /// <summary>
    /// The conditional record decision 076 asked for: under EclipseLink a lazy reference is
    /// inert without the weaving agent, so the loss says both that the strategy does not
    /// travel and that it may never have been in effect in the source either.
    /// </summary>
    [Fact]
    public void ALazyReferenceIsReportedWithTheWeavingItWouldHaveNeeded()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;

            @Entity
            public class CustomerOrder {
                @Id
                private Integer OrderId;

                @ManyToOne(fetch = FetchType.LAZY)
                @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId")
                private Customer Customer;
            }
            """);

        var record = Assert.Single(builder.Records, r => r.Reason.Contains("weaves its bytecode"));

        Assert.Equal(ConversionRecordKind.Loss, record.Kind);
        Assert.Equal("Customer", record.Property);
    }

    /// <summary>The same source under the other implementation says nothing: there the annotation does what it promises.</summary>
    [Fact]
    public void TheSameLazyReferenceIsSilentUnderHibernate()
    {
        var source = """
            import jakarta.persistence.*;

            @Entity
            public class CustomerOrder {
                @Id
                private Integer OrderId;

                @ManyToOne(fetch = FetchType.LAZY)
                @JoinColumn(name = "CustomerId", referencedColumnName = "CustomerId")
                private Customer Customer;
            }
            """;

        var builder = new DummyEntityBuilder();
        new HibernateEntityParser(builder, new JpaReadingContext()).Parse(source);

        Assert.DoesNotContain(builder.Records, r => r.Reason.Contains("weaving") || r.Reason.Contains("weaves"));
    }

    /// <summary>An eager reference, or one that states no strategy at all, is nobody's warning.</summary>
    [Fact]
    public void AReferenceWithoutLazyLoadingIsNotReported()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;

            @Entity
            public class CustomerOrder {
                @Id
                private Integer OrderId;

                @ManyToOne(fetch = FetchType.EAGER)
                private Customer Customer;

                @OneToOne
                private Invoice Invoice;
            }
            """);

        Assert.DoesNotContain(builder.Records, r => r.Reason.Contains("weaves its bytecode"));
    }

    /// <summary>
    /// A lazy collection is a different matter and stays silent: collection indirection
    /// needs no weaving, and the strategy is out of the model for everyone anyway.
    /// </summary>
    [Fact]
    public void ALazyCollectionIsNotTheWarning()
    {
        var (builder, _) = Parse("""
            import jakarta.persistence.*;
            import java.util.List;

            @Entity
            public class Customer {
                @Id
                private Integer CustomerId;

                @OneToMany(mappedBy = "Customer", fetch = FetchType.LAZY)
                private List<CustomerOrder> Orders;
            }
            """);

        Assert.DoesNotContain(builder.Records, r => r.Reason.Contains("weaves its bytecode"));
    }

    /// <summary>
    /// The standard descriptor is read by the same shared parser for both implementations,
    /// and its precedence is the specification's (decisions 068 and 077): the value in XML
    /// stands and the annotation that says otherwise is a conflict.
    /// </summary>
    [Fact]
    public void TheStandardOrmXmlIsReadBeforeTheClass()
    {
        var builder = new DummyEntityBuilder();
        var context = new JpaReadingContext();

        new JpaOrmXmlParser(builder, context).Parse("""
            <?xml version="1.0" encoding="UTF-8"?>
            <entity-mappings xmlns="https://jakarta.ee/xml/ns/persistence/orm" version="3.2">
                <package>Shop</package>
                <entity class="Customer">
                    <table name="Customers" schema="Sales"/>
                    <attributes>
                        <id name="CustomerId">
                            <column name="CustomerId"/>
                        </id>
                    </attributes>
                </entity>
            </entity-mappings>
            """);

        new EclipseLinkEntityParser(builder, context).Parse("""
            package Shop;

            import jakarta.persistence.*;

            @Entity
            public class Customer {
                @Id
                private Integer CustomerId;
            }
            """);

        var map = builder.EntityMaps.Single();

        Assert.Equal("Customers", map.Table);
        Assert.Equal("Sales", map.Schema);
        Assert.Equal("CustomerId", Map(map, "CustomerId").ColumnName);
    }
}
