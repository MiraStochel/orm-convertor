using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Catalog;

/// <summary>
/// The control side of decision 015 over a fake catalog: what the completion phase writes
/// into the intermediate representation, what it leaves alone, and what it reports. The
/// mechanism - the SQL Server reader - is judged separately against the test database.
/// </summary>
public class CatalogCompletionTest
{
    private const string CustomerSource = """
        namespace DapperEntities;

        public class Customer
        {
            public int CustomerId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Notes { get; set; }
        }
        """;

    private static TableImage CustomersImage() => new()
    {
        Schema = "sales",
        Name = "Customers",
        Columns =
        [
            new ColumnImage { Name = "CustomerId", Type = DatabaseType.Int, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "Name", Type = DatabaseType.NVarChar, Length = 100, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "Notes", Type = DatabaseType.NVarChar, Length = 400, IsNullable = true, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["CustomerId"],
        ForeignKeys = [],
    };

    private static NHibernateEntityBuilder ParseCustomer()
    {
        var builder = new NHibernateEntityBuilder();
        new DapperEntityParser(builder).Parse(CustomerSource);
        return builder;
    }

    [Fact]
    public void SuppliesTableSchemaColumnsAndKeyFromTheCatalog()
    {
        var builder = ParseCustomer();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        var em = builder.EntityMaps.Single();
        Assert.Equal("Customers", em.Table);
        Assert.Equal("sales", em.Schema);

        var name = em.PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.Equal(DatabaseType.NVarChar, name.Type);
        Assert.Equal(100, name.Length);
        Assert.False(name.IsNullable);

        var notes = em.PropertyMaps.Single(pm => pm.Property.Name == "Notes");
        Assert.True(notes.IsNullable);

        var key = Assert.Single(em.PrimaryKey!.Parts);
        Assert.Equal("CustomerId", key.PropertyMap.Property.Name);
        Assert.Equal(PrimaryKeyStrategy.Identity, key.Strategy);

        // Every supplied fact carries its origin as a record, not as model state (decision 010).
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.PrimaryKey);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.TableName);
    }

    [Fact]
    public void ColumnNameEqualToThePropertyStaysImplicit()
    {
        var builder = ParseCustomer();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        // Every builder falls back to the property name, so writing the identical name
        // would state nothing (decision 015).
        Assert.All(builder.EntityMaps.Single().PropertyMaps, pm => Assert.Null(pm.ColumnName));
    }

    [Fact]
    public void SourceOutranksTheCatalogAndTheDisagreementIsReported()
    {
        var builder = ParseCustomer();
        builder.SetPropertyDatabaseMapping("Name", new Dictionary<string, string>
        {
            ["type"] = ((int)DatabaseType.VarChar).ToString(),
        });

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        var name = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.Equal(DatabaseType.VarChar, name.Type);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal(MappingFactCategory.DatabaseType, conflict.Category);
        Assert.Equal("Name", conflict.Property);
    }

    [Fact]
    public void CompletionIsIdempotent()
    {
        var builder = ParseCustomer();
        var reader = new FakeCatalogReader(CustomersImage());

        CatalogCompletion.Complete(builder, reader);
        var suppliedOnce = builder.Records.Count(r => r.Kind == ConversionRecordKind.Supplied);

        CatalogCompletion.Complete(builder, reader);

        // A fact once present is never overwritten, so the second pass supplies nothing
        // and reports nothing new (decision 015).
        Assert.Equal(suppliedOnce, builder.Records.Count(r => r.Kind == ConversionRecordKind.Supplied));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void EmptyDemandMeansZeroQueries()
    {
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(CustomerSource);
        var reader = new FakeCatalogReader(CustomersImage());

        var elapsed = CatalogCompletion.Complete(builder, reader);

        // Dapper as a target expresses no mapping category, so its demand is empty and
        // the catalog is never asked (decision 015).
        Assert.Equal(0, reader.Reads);
        Assert.Null(elapsed);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Supplied);
    }

    [Fact]
    public void MissingConnectionBecomesARecordNotAFailure()
    {
        var builder = ParseCustomer();

        var elapsed = CatalogCompletion.Complete(builder, reader: null);

        Assert.Null(elapsed);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("No database connection"));
    }

    [Fact]
    public void SynthesizesARelationForACatalogForeignKeyWithANavigationProperty()
    {
        const string orderSource = """
            namespace DapperEntities;

            public class Order
            {
                public int OrderId { get; set; }

                public int CustomerId { get; set; }

                public Customer Customer { get; set; } = null!;
            }
            """;

        var ordersImage = new TableImage
        {
            Schema = "sales",
            Name = "Orders",
            Columns =
            [
                new ColumnImage { Name = "OrderId", Type = DatabaseType.Int, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Int, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["OrderId"],
            ForeignKeys =
            [
                new ForeignKeyImage
                {
                    Name = "FK_Orders_Customers",
                    ReferencedSchema = "sales",
                    ReferencedTable = "Customers",
                    Columns = [new ForeignKeyColumn("CustomerId", "CustomerId")],
                },
            ],
        };

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(CustomerSource);
        parser.Parse(orderSource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), ordersImage));

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        var relation = Assert.Single(order.Relations);
        Assert.Equal(Cardinality.ManyToOne, relation.Cardinality);
        Assert.Equal(RelationRole.Owning, relation.Role);
        Assert.Equal("Customer", relation.TargetEntity);
        Assert.Equal("Customer", relation.SourceNavigationProperty);

        // The pairs land through the resolution phase, which runs inside Build (decision 001).
        builder.Build();
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("CustomerId", pair.Source.Property.Name);

        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.ForeignKeyColumns);
    }

    [Fact]
    public void ForeignKeyWithoutANavigationPropertyIsReportedNotInvented()
    {
        const string orderSource = """
            namespace DapperEntities;

            public class Order
            {
                public int OrderId { get; set; }

                public int CustomerId { get; set; }
            }
            """;

        var ordersImage = new TableImage
        {
            Schema = "sales",
            Name = "Orders",
            Columns =
            [
                new ColumnImage { Name = "OrderId", Type = DatabaseType.Int, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Int, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["OrderId"],
            ForeignKeys =
            [
                new ForeignKeyImage
                {
                    Name = "FK_Orders_Customers",
                    ReferencedSchema = "sales",
                    ReferencedTable = "Customers",
                    Columns = [new ForeignKeyColumn("CustomerId", "CustomerId")],
                },
            ],
        };

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(CustomerSource);
        parser.Parse(orderSource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage(), ordersImage));

        var order = builder.EntityMaps.Single(em => em.Entity.Name == "Order");
        Assert.Empty(order.Relations);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Category == MappingFactCategory.ForeignKeyColumns
            && r.Entity == "Order");
    }

    [Fact]
    public void InfersTheLanguageTypeOfAPropertyKnownOnlyToTheMapping()
    {
        // A property that exists only in the mapping descriptor has a database type but
        // no language type; the inference is a third-level convention and runs even
        // without any catalog (decision 015).
        var builder = new NHibernateEntityBuilder();
        builder.BeginEntity();
        builder.AddClassHeader("public", "Invoice");
        builder.SetPropertyDatabaseMapping("Total", new Dictionary<string, string>
        {
            ["type"] = ((int)DatabaseType.Decimal).ToString(),
            ["nullable"] = "false",
        });

        CatalogCompletion.Complete(builder, reader: null);

        var total = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Total");
        Assert.Equal(ScalarType.Decimal, total.Property.Type?.ScalarType);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Property == "Total" && r.Reason.Contains("inferred"));
    }
}
