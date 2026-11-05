using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EclipseLinkWrappers;
using HibernateWrappers;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;

namespace Tests.Hibernate;

/// <summary>
/// The JPA spelling of a primary key that is at the same time the foreign key of a reference
/// (decision 206). A single-part key shared with a reference is @MapsId over a writable join
/// column: under @MapsId the identifier is derived from the reference and the join column is
/// the mapping that writes the key, so the read-only idiom of decision 012 - the right one
/// for a scalar beside its relation - would leave no mapping that writes it, which EclipseLink
/// 5.0.0 refuses at deployment (EclipseLink-46) and Hibernate 7.4.5 tolerates. A composite key
/// shared whole has no @MapsId: EclipseLink refuses @MapsId over an @IdClass (EclipseLink-48),
/// so both profiles write the identifier parts and the reference read-only over them, as for a
/// composite key with one shared part. Measured by SharedKeyClaimsTest of both implementations
/// in the Java suite; the same shared layer writes both profiles, which the EclipseLink test
/// here asserts.
/// </summary>
public class HibernateSharedKeyTest
{
    /// <summary>Builds every entity and returns the dependent one's artifact.</summary>
    private static string Dependent(AbstractJpaEntityBuilder builder, Action<AbstractJpaEntityBuilder> populate, string className)
    {
        populate(builder);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts
            .Where(a => a.ContentType == ConversionContentType.JavaEntity)
            .Select(a => a.Content.Replace("\r\n", "\n"))
            .Single(c => c.Contains($"class {className} {{"));
    }

    private static void Customer(AbstractJpaEntityBuilder b)
    {
        b.AddClassHeader("public", "Customer");
        b.AddTable("Customers");
        b.AddProperty("long", "CustomerId", "public", hasGetter: true, hasSetter: true);
        b.AddProperty("CustomerProfile", "Profile", "public", hasGetter: true, hasSetter: true, isNullable: true);
        b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "CustomerId");
        b.AddForeignKey(Cardinality.OneToOne, "Profile", "CustomerProfile", RelationRole.Inverse);
    }

    /// <summary>The shape an EF Core source gives: the key column stated as the foreign key of the reference.</summary>
    private static void ProfileKeyedByTheCustomer(AbstractJpaEntityBuilder b)
    {
        Customer(b);
        b.BeginEntity();
        b.AddClassHeader("public", "CustomerProfile");
        b.AddTable("CustomerProfiles");
        b.AddProperty("long", "CustomerId", "public", hasGetter: true, hasSetter: true);
        b.AddProperty("Customer", "Customer", "public", hasGetter: true, hasSetter: true);
        b.AddProperty("string", "Bio", "public", hasGetter: true, hasSetter: true, isNullable: true);
        b.AddPrimaryKey(PrimaryKeyStrategy.Assigned, "CustomerId");
        b.AddForeignKey(Cardinality.OneToOne, "Customer", "Customer", RelationRole.Owning, foreignKeyColumns: ["CustomerId"]);
    }

    [Fact]
    public void ASharedKeyIsMapsIdOverAWritableJoinColumn()
    {
        var code = Dependent(new HibernateEntityBuilder(), ProfileKeyedByTheCustomer, "CustomerProfile");

        Assert.Contains("@Id\n    @Column(name = \"CustomerId\")\n    private Long CustomerId;", code);
        Assert.Contains("@OneToOne\n    @MapsId\n    @JoinColumn(name = \"CustomerId\", referencedColumnName = \"CustomerId\", unique = true)\n    private Customer Customer;", code);
        Assert.DoesNotContain("insertable = false", code);
        Assert.DoesNotContain("@GeneratedValue", code);
    }

    /// <summary>The same shared layer writes both profiles: EclipseLink is the implementation the read-only spelling failed under.</summary>
    [Fact]
    public void EclipseLinkWritesTheSameSharedKey()
    {
        var code = Dependent(new EclipseLinkEntityBuilder(), ProfileKeyedByTheCustomer, "CustomerProfile");

        Assert.Contains("@MapsId\n    @JoinColumn(name = \"CustomerId\", referencedColumnName = \"CustomerId\", unique = true)", code);
        Assert.DoesNotContain("insertable = false", code);
    }

    /// <summary>
    /// The shape an NHibernate source gives: the foreign generator names the navigation and
    /// the one-to-one states no column. The join column is the entity's own key column, which
    /// is what @MapsId means and what the JPA parser reads it back as; the derived
    /// navigation_column name of a reference without columns would be a column the table does
    /// not have.
    /// </summary>
    [Fact]
    public void AForeignGeneratorIsMapsIdOverTheKeyColumn()
    {
        var builder = new HibernateEntityBuilder();
        var code = Dependent(builder, b =>
        {
            Customer(b);
            b.BeginEntity();
            b.AddClassHeader("public", "CustomerProfile");
            b.AddTable("CustomerProfiles");
            b.AddProperty("long", "CustomerID", "public", hasGetter: true, hasSetter: true);
            b.SetPropertyDatabaseMapping("CustomerID", new() { ["column"] = "ProfileCustomerId" });
            b.AddProperty("Customer", "Owner", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "CustomerID");
            b.SetKeyStrategyDetails("CustomerID", sourceStrategyName: "foreign", sourceParameters: new Dictionary<string, string> { ["property"] = "Owner" });
            b.AddForeignKey(Cardinality.OneToOne, "Owner", "Customer", RelationRole.Owning);
        }, "CustomerProfile");

        Assert.Contains("@Id\n    @Column(name = \"ProfileCustomerId\")\n    private Long CustomerID;", code);
        Assert.Contains("@OneToOne\n    @MapsId\n    @JoinColumn(name = \"ProfileCustomerId\", referencedColumnName = \"CustomerId\", unique = true)\n    private Customer Owner;", code);
        Assert.DoesNotContain("Owner_", code);
        Assert.DoesNotContain("insertable = false", code);
        Assert.DoesNotContain("@GeneratedValue", code);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.ForeignKeyColumns);
    }

    /// <summary>A composite key shared whole: no @MapsId, the identifier writes the columns and the reference is read-only over them.</summary>
    [Fact]
    public void ACompositeKeySharedWholeIsWrittenWithoutMapsId()
    {
        var code = Dependent(new HibernateEntityBuilder(), b =>
        {
            b.AddClassHeader("public", "Pair");
            b.AddTable("Pairs");
            b.AddProperty("int", "A", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "B", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey([("A", 0, PrimaryKeyStrategy.Assigned), ("B", 1, PrimaryKeyStrategy.Assigned)]);

            b.BeginEntity();
            b.AddClassHeader("public", "PairDetail");
            b.AddTable("PairDetails");
            b.AddProperty("int", "A", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "B", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("Pair", "Pair", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey([("A", 0, PrimaryKeyStrategy.Assigned), ("B", 1, PrimaryKeyStrategy.Assigned)]);
            b.AddForeignKey(Cardinality.OneToOne, "Pair", "Pair", RelationRole.Owning, foreignKeyColumns: ["A", "B"]);
        }, "PairDetail");

        Assert.DoesNotContain("@MapsId", code);
        Assert.Contains("@IdClass(PairDetail.PairDetailId.class)", code);
        Assert.Contains("@JoinColumn(name = \"A\", referencedColumnName = \"A\", unique = true, insertable = false, updatable = false),", code);
        Assert.Contains("@JoinColumn(name = \"B\", referencedColumnName = \"B\", unique = true, insertable = false, updatable = false)", code);
    }

    /// <summary>A composite key with one shared part keeps the read-only spelling of decision 012.</summary>
    [Fact]
    public void ACompositeKeyWithOneSharedPartKeepsTheReferenceReadOnly()
    {
        var code = Dependent(new HibernateEntityBuilder(), b =>
        {
            b.AddClassHeader("public", "Order");
            b.AddTable("Orders");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderId");

            b.BeginEntity();
            b.AddClassHeader("public", "OrderLine");
            b.AddTable("OrderLines");
            b.AddProperty("int", "OrderId", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("int", "LineNumber", "public", hasGetter: true, hasSetter: true);
            b.AddProperty("Order", "Order", "public", hasGetter: true, hasSetter: true);
            b.AddPrimaryKey([("OrderId", 0, PrimaryKeyStrategy.Assigned), ("LineNumber", 1, PrimaryKeyStrategy.Assigned)]);
            b.AddForeignKey(Cardinality.ManyToOne, "Order", "Order", RelationRole.Owning, foreignKeyColumns: ["OrderId"]);
        }, "OrderLine");

        Assert.DoesNotContain("@MapsId", code);
        Assert.Contains("@ManyToOne\n    @JoinColumn(name = \"OrderId\", referencedColumnName = \"OrderId\", insertable = false, updatable = false)\n    private Order Order;", code);
    }
}
