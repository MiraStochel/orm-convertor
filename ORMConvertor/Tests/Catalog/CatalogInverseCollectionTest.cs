using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Catalog;

/// <summary>
/// The inverse side of a collection over the catalog (decisions 012 and 015): the key
/// columns of a collection live in the child's table, so the completion phase reads them
/// from the child's foreign keys - it completes an existing inverse one-to-many with its
/// column pairs and synthesizes the relation where the source declares a collection
/// navigation. No member is invented: without the collection nothing is derived.
/// </summary>
public class CatalogInverseCollectionTest
{
    private const string CustomerSource = """
        namespace DapperEntities;

        public class Customer
        {
            public int CustomerId { get; set; }

            public List<Order> Orders { get; set; } = [];
        }
        """;

    private const string OrderSource = """
        namespace DapperEntities;

        public class Order
        {
            public int OrderId { get; set; }

            public int CustId { get; set; }

            public Customer Customer { get; set; } = null!;
        }
        """;

    private static TableImage CustomersImage() => new()
    {
        Schema = "sales",
        Name = "Customers",
        Columns =
        [
            new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
        ],
        PrimaryKeyColumns = ["CustomerId"],
        ForeignKeys = [],
    };

    // The foreign key column is named differently from the owner's key column on purpose:
    // it is what tells the catalog's fact apart from the fallback of decision 012, which
    // writes the owner's key column into <key>.
    private static TableImage OrdersImage() => new()
    {
        Schema = "sales",
        Name = "Orders",
        Columns =
        [
            new ColumnImage { Name = "OrderId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "CustId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["OrderId"],
        ForeignKeys =
        [
            new ForeignKeyImage
            {
                Name = "FK_Orders_Customers",
                ReferencedSchema = "sales",
                ReferencedTable = "Customers",
                Columns = [new ForeignKeyColumn("CustId", "CustomerId")],
            },
        ],
    };

    private static NHibernateEntityBuilder ParseCustomerAndOrder()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(CustomerSource);
        parser.Parse(OrderSource);
        return builder;
    }

    [Fact]
    public void SynthesizesTheInverseCollectionFromTheChildsForeignKey()
    {
        var builder = ParseCustomerAndOrder();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));

        // The source declares the collection, the schema the relation.
        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        var relation = Assert.Single(customer.Relations);
        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("Order", relation.TargetEntity);
        Assert.Equal("Orders", relation.SourceNavigationProperty);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Entity == "Customer" && r.Property == "Orders");

        // The pairs land through the resolution phase, which runs inside Build (decision 001);
        // their source side is the child's column (decision 012).
        builder.Build();
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustId", pair.Source.Property.Name);
        Assert.Equal("CustomerId", pair.Target.Property.Name);
    }

    [Fact]
    public void TheChildsKeyColumnReachesTheBagInsteadOfTheFallback()
    {
        var builder = ParseCustomerAndOrder();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));
        var outputs = builder.Build();

        var mapping = outputs.Single(o =>
            o.ContentType == ConversionContentType.XML && o.Content.Contains("<bag"));
        Assert.Contains("<key column=\"CustId\" />", mapping.Content);

        // With the fact supplied, the fallback of decision 012 - the owner's key column -
        // has no reason to fire, and neither has its convention record.
        Assert.DoesNotContain("<key column=\"CustomerId\" />", mapping.Content);
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("owner's key column"));
    }

    [Fact]
    public void CompletesTheKeyColumnsOfAStatedUnidirectionalCollection()
    {
        // The source states the collection relation but not its key columns, and no owning
        // reference on the far side holds them; the catalog fills the pairs during
        // completion, before the resolution phase runs.
        const string orderWithoutNavigation = """
            namespace DapperEntities;

            public class Order
            {
                public int OrderId { get; set; }

                public int CustId { get; set; }
            }
            """;

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(CustomerSource);
        parser.Parse(orderWithoutNavigation);
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        builder.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", RelationRole.Inverse);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));

        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        var relation = Assert.Single(customer.Relations);
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustId", pair.Source.Property.Name);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Entity == "Customer" && r.Property == "Orders");
    }

    /// <summary>
    /// A stated collection whose owning counterpart on the far side carries the columns is
    /// not open (decision 012): the catalog completes the counterpart, the collection keeps no
    /// columns of its own and claims none, and the bag takes its key from the counterpart.
    /// </summary>
    [Fact]
    public void AStatedInverseCollectionTakesItsKeyFromTheOwningSide()
    {
        var builder = ParseCustomerAndOrder();
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        builder.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", RelationRole.Inverse);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));

        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.Empty(Assert.Single(customer.Relations).ColumnPairs);
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Entity == "Customer" && r.Property == "Orders");

        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML && o.Content.Contains("<bag"));
        Assert.Contains("<key column=\"CustId\" />", mapping.Content);
    }

    [Fact]
    public void StatedKeyColumnsThatDisagreeWithTheCatalogAreReportedNotReplaced()
    {
        var builder = ParseCustomerAndOrder();
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        builder.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", RelationRole.Inverse,
            foreignKeyColumns: ["CustomerNumber"]);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));

        // The source outranks the catalog (rule E9, decision 015): the stated columns stay
        // and the disagreement becomes a record.
        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.Empty(Assert.Single(customer.Relations).ColumnPairs);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Conflict
            && r.Entity == "Customer"
            && r.Category == MappingFactCategory.ForeignKeyColumns);
    }

    [Fact]
    public void WithoutACollectionNavigationTheInverseSideIsNotInvented()
    {
        const string bareCustomer = """
            namespace DapperEntities;

            public class Customer
            {
                public int CustomerId { get; set; }
            }
            """;

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(bareCustomer);
        parser.Parse(OrderSource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), OrdersImage()));

        // The owning side carries the relation; a parent without a collection is a fact of
        // the source - a unidirectional relation - not a gap to report.
        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.Empty(customer.Relations);
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Entity == "Customer");
    }

    [Fact]
    public void ASharedKeyForeignKeyIsNoCollection()
    {
        // The profile's whole primary key is the foreign key: a shared-key one-to-one,
        // whose inverse side is a reference, never a collection (decision 012) - even
        // when the source declares one.
        const string customerWithProfiles = """
            namespace DapperEntities;

            public class Customer
            {
                public int CustomerId { get; set; }

                public List<Profile> Profiles { get; set; } = [];
            }
            """;

        const string profileSource = """
            namespace DapperEntities;

            public class Profile
            {
                public int CustomerId { get; set; }

                public Customer Customer { get; set; } = null!;
            }
            """;

        var profilesImage = new TableImage
        {
            Schema = "sales",
            Name = "Profiles",
            Columns =
            [
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["CustomerId"],
            ForeignKeys =
            [
                new ForeignKeyImage
                {
                    Name = "FK_Profiles_Customers",
                    ReferencedSchema = "sales",
                    ReferencedTable = "Customers",
                    Columns = [new ForeignKeyColumn("CustomerId", "CustomerId")],
                },
            ],
        };

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(customerWithProfiles);
        parser.Parse(profileSource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), profilesImage));

        var customer = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        Assert.Empty(customer.Relations);

        // The owning side still gets its one-to-one from the same foreign key.
        var profile = builder.EntityMaps.Single(em => em.Entity.Name == "Profile");
        var owning = Assert.Single(profile.Relations);
        Assert.Equal(Cardinality.OneToOne, owning.Cardinality);
    }

    /// <summary>
    /// The counterpart is found the way the builders find it - among the child's owning
    /// many-to-one and one-to-one references towards the parent -, so two of them and no name
    /// leave the collection open: the catalog supplies its columns, and the JPA builder tells
    /// the two references apart by them and writes mappedBy. Counting only the many-to-one, the
    /// phase took it for the counterpart and supplied nothing, and the builder, still seeing two
    /// candidates and no columns, fell back to a join column of its own convention.
    /// </summary>
    [Fact]
    public void TwoOwningReferencesTowardsTheParentLeaveTheCollectionToTheCatalog()
    {
        const string order = """
            namespace DapperEntities;

            public class Order
            {
                public int OrderId { get; set; }

                public int CustId { get; set; }

                public int PrefId { get; set; }

                public Customer Customer { get; set; } = null!;

                public Customer Preferred { get; set; } = null!;
            }
            """;

        var builder = new HibernateWrappers.HibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(CustomerSource);
        parser.Parse(order);
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        builder.AddForeignKey(Cardinality.ManyToOne, "Customer", "Customer", RelationRole.Owning, ["CustId"]);
        builder.AddForeignKey(Cardinality.OneToOne, "Preferred", "Customer", RelationRole.Owning, ["PrefId"]);
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Customer");
        builder.AddForeignKey(Cardinality.OneToMany, "Orders", "Order", RelationRole.Inverse);

        var withPreferred = OrdersImage();
        var orders = new TableImage
        {
            Schema = withPreferred.Schema,
            Name = withPreferred.Name,
            Columns = [.. withPreferred.Columns, new ColumnImage { Name = "PrefId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false }],
            PrimaryKeyColumns = withPreferred.PrimaryKeyColumns,
            ForeignKeys = withPreferred.ForeignKeys,
        };

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), orders));

        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Entity == "Customer" && r.Property == "Orders");

        var customer = builder.Build().Single(o => o.ContentType == ConversionContentType.JavaEntity && o.Content.Contains("class Customer"));
        Assert.Contains("@OneToMany(mappedBy = \"Customer\")", customer.Content);
    }

    /* ---- a collection whose owning side the source states (decisions 012, 015, 017) ---- */

    private const string JpaCustomer = """
        package Shop;

        import jakarta.persistence.*;
        import java.util.List;

        @Entity
        @Table(name = "Customers")
        public class Customer {
            @Id
            @Column(name = "CustomerID")
            private Integer id;

            @OneToMany(mappedBy = "customer")
            private List<Order> orders;
        }
        """;

    private const string JpaOrder = """
        package Shop;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Orders")
        public class Order {
            @Id
            private Integer id;

            @ManyToOne
            @JoinColumn(name = "CustRef")
            private Customer customer;
        }
        """;

    private static TableImage JpaCustomersImage() => new()
    {
        Schema = "dbo",
        Name = "Customers",
        Columns = [new ColumnImage { Name = "CustomerID", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false }],
        PrimaryKeyColumns = ["CustomerID"],
        ForeignKeys = [],
    };

    private static TableImage JpaOrdersImage(string foreignKeyColumn) => new()
    {
        Schema = "dbo",
        Name = "Orders",
        Columns =
        [
            new ColumnImage { Name = "id", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = foreignKeyColumn, Type = DatabaseType.Integer, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["id"],
        ForeignKeys =
        [
            new ForeignKeyImage
            {
                Name = "FK_Orders_Customers",
                ReferencedSchema = "dbo",
                ReferencedTable = "Customers",
                Columns = [new ForeignKeyColumn(foreignKeyColumn, "CustomerID")],
            },
        ],
    };

    private static OrmConvertor.ConversionResult ConvertJpa(ORMEnum target, string foreignKeyColumn, string? query = null)
        => OrmConvertor.ConversionHandler.Convert(
            ORMEnum.Hibernate,
            target,
            [
                new ConversionSource { Content = JpaCustomer, ContentType = ConversionContentType.Java },
                new ConversionSource { Content = JpaOrder, ContentType = ConversionContentType.Java },
                .. query is null ? Array.Empty<ConversionSource>() : [new ConversionSource { Content = query, ContentType = ConversionContentType.JpqlQuery }],
            ],
            new FakeCatalogReader(JpaCustomersImage(), JpaOrdersImage(foreignKeyColumn)));

    /// <summary>
    /// A source whose join column disagrees with the schema keeps its column on the owning
    /// side with a conflict (rule E9), and the collection follows the owning side instead of
    /// taking the schema's column: one foreign key, one column, in the mapping of both.
    /// </summary>
    [Fact]
    public void ACollectionFollowsItsOwningSideWhereTheSourceDisagreesWithTheSchema()
    {
        var result = ConvertJpa(ORMEnum.NHibernate, "CustomerID");

        var customer = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.XML && s.Content.Contains("<class name=\"Customer\"")).Content;
        var order = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.XML && s.Content.Contains("<class name=\"Order\"")).Content;

        Assert.Contains("column=\"CustRef\"", order);
        Assert.Contains("<key column=\"CustRef\" />", customer);
        Assert.DoesNotContain("<key column=\"CustomerID\" />", customer);
        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.ForeignKeyColumns && r.Property == "customer");
        Assert.DoesNotContain(result.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.ForeignKeyColumns && r.Property == "orders");
    }

    /// <summary>The path join says the same in both directions: the source's column, whichever side it starts from.</summary>
    [Theory]
    [InlineData("select o from Order o join o.customer c")]
    [InlineData("select c from Customer c join c.orders o")]
    public void BothDirectionsOfThePathJoinNameTheSameColumn(string query)
    {
        var sql = Assert.Single(ConvertJpa(ORMEnum.Dapper, "CustomerID", query).Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("o.CustRef = c.CustomerID", sql);
    }

    /// <summary>
    /// A schema that agrees with the source changes nothing on the collection either: the
    /// columns are the source's, so no record says the catalog supplied them.
    /// </summary>
    [Fact]
    public void AnAgreeingSchemaSuppliesNothingToTheCollection()
    {
        var result = ConvertJpa(ORMEnum.NHibernate, "CustRef");

        Assert.DoesNotContain(result.Records, r =>
            r.Category == MappingFactCategory.ForeignKeyColumns && r.Kind is ConversionRecordKind.Supplied or ConversionRecordKind.Conflict);
    }
}
