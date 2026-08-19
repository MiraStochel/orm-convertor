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
    public void CompletesTheKeyColumnsOfAStatedInverseCollection()
    {
        // The source states the collection relation but not its key columns; the catalog
        // fills the pairs during completion, before the resolution phase runs.
        var builder = ParseCustomerAndOrder();
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
}
