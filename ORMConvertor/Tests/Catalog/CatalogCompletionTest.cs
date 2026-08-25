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
            new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "Notes", Type = DatabaseType.VarChar, IsUnicode = true, Length = 400, IsNullable = true, IsIdentity = false },
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

        var result = CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        Assert.Equal(CatalogConnectionState.Reached, result.ConnectionState);
        Assert.NotNull(result.ReadTime);

        var em = builder.EntityMaps.Single();
        Assert.Equal("Customers", em.Table);
        Assert.Equal("sales", em.Schema);

        var name = em.PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.Equal(DatabaseType.VarChar, name.Type);
        Assert.True(name.IsUnicode);
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

        // The source claims a non-unicode column; the catalog states a unicode one. The
        // unicode facet is part of the type claim (decision 019), so the disagreement is
        // a conflict of the DatabaseType category.
        builder.SetPropertyDatabaseType("Name", DatabaseType.VarChar, isUnicode: false);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        var name = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Name");
        Assert.Equal(DatabaseType.VarChar, name.Type);
        Assert.False(name.IsUnicode);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal(MappingFactCategory.DatabaseType, conflict.Category);
        Assert.Equal("Name", conflict.Property);
    }

    /// <summary>
    /// A stated "no key" is the source answering the key question, not leaving it empty:
    /// the catalog must not supply the key it knows, and the disagreement is a conflict
    /// record with the source winning (decision 063).
    /// </summary>
    [Fact]
    public void StatedKeylessnessKeepsTheCatalogKeyOut()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreWrappers.EFCoreEntityParser(builder).Parse("""
            namespace EFCoreEntities;

            using Microsoft.EntityFrameworkCore;

            [Keyless]
            public class Customer
            {
                public int CustomerId { get; set; }
            }
            """);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(CustomersImage()));

        var em = builder.EntityMaps.Single();
        Assert.Null(em.PrimaryKey);
        Assert.True(em.HasNoKey);

        var conflict = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
        Assert.Equal(MappingFactCategory.PrimaryKey, conflict.Category);
    }

    private static TableImage ProductsImage(bool keyHasDefault = false) => new()
    {
        Schema = "dbo",
        Name = "Products",
        Columns =
        [
            new ColumnImage
            {
                Name = "ProductId",
                Type = DatabaseType.Integer,
                IsNullable = false,
                IsIdentity = false,
                HasDefault = keyHasDefault,
            },
        ],
        PrimaryKeyColumns = ["ProductId"],
        ForeignKeys = [],
    };

    /// <summary>
    /// The complement of the identity supply (decision 064): a key column that is neither
    /// IDENTITY nor backed by a default is a positive statement of the schema - the value
    /// must arrive with the INSERT - so the phase states Assigned instead of leaving the
    /// two target conventions to contradict each other.
    /// </summary>
    [Fact]
    public void SuppliesAssignedForAKeyColumnTheStoreDoesNotFill()
    {
        // The fresh-key path: a Dapper source states no key at all, so the whole key
        // arrives from the catalog with the strategy the column states.
        var builder = new NHibernateEntityBuilder();
        new DapperEntityParser(builder).Parse("""
            namespace DapperEntities;

            public class Product
            {
                public int ProductId { get; set; }
            }
            """);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(ProductsImage()));

        var part = Assert.Single(builder.EntityMaps.Single().PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Assigned, part.Strategy);
    }

    [Fact]
    public void SuppliesAssignedToAStatedKeyPartLeftUnspecified()
    {
        // The existing-key path: the source states the key but not how its value arises.
        var builder = new NHibernateEntityBuilder();
        builder.BeginEntity();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("Products");
        builder.AddProperty("int", "ProductId", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "ProductId");

        CatalogCompletion.Complete(builder, new FakeCatalogReader(ProductsImage()));

        var part = Assert.Single(builder.EntityMaps.Single().PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Assigned, part.Strategy);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied
            && r.Category == MappingFactCategory.PrimaryKeyStrategy
            && r.Property == "ProductId");
    }

    [Fact]
    public void AKeyColumnBackedByADefaultStaysUnspecifiedAndIsReported()
    {
        var builder = new NHibernateEntityBuilder();
        builder.BeginEntity();
        builder.AddClassHeader("public", "Product");
        builder.AddTable("Products");
        builder.AddProperty("int", "ProductId", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "ProductId");

        CatalogCompletion.Complete(builder, new FakeCatalogReader(ProductsImage(keyHasDefault: true)));

        // The store can fill the column, but a boolean flag cannot name the mechanism:
        // Assigned here would be the false claim decision 064 forbids, so the strategy
        // stays unspecified and the state is a record, not a guess.
        var part = Assert.Single(builder.EntityMaps.Single().PrimaryKey!.Parts);
        Assert.Equal(PrimaryKeyStrategy.Unspecified, part.Strategy);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Category == MappingFactCategory.PrimaryKeyStrategy
            && r.Property == "ProductId");
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.PrimaryKeyStrategy);
    }

    [Fact]
    public void SuppliesTheVersionFlagForARowversionColumn()
    {
        var builder = new NHibernateEntityBuilder();
        new DapperEntityParser(builder).Parse("""
            namespace DapperEntities;

            public class Document
            {
                public int DocumentId { get; set; }

                public byte[] RowVersion { get; set; }
            }
            """);
        var reader = new FakeCatalogReader(new TableImage
        {
            Schema = "dbo",
            Name = "Documents",
            Columns =
            [
                new ColumnImage { Name = "DocumentId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage
                {
                    Name = "RowVersion",
                    Type = DatabaseType.VarBinary,
                    SourceSqlType = "rowversion",
                    Length = 8,
                    IsNullable = false,
                    IsIdentity = false,
                    IsRowVersion = true,
                },
            ],
            PrimaryKeyColumns = ["DocumentId"],
            ForeignKeys = [],
        });

        CatalogCompletion.Complete(builder, reader);

        // The versioning claim arrives beside the type, not as one (decision 019): the
        // family stays binary and the flag is the schema's own fact (decision 030).
        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "RowVersion");
        Assert.True(map.IsVersion);
        Assert.Equal(DatabaseType.VarBinary, map.Type);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.VersionColumn);

        // A flag once present is never re-supplied (decision 015).
        CatalogCompletion.Complete(builder, reader);
        Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.VersionColumn);
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

        var result = CatalogCompletion.Complete(builder, reader);

        // Dapper as a target expresses no mapping category, so its demand is empty and
        // the catalog is never asked (decision 015). The connection state says exactly
        // that: configured, but never tried.
        Assert.Equal(0, reader.Reads);
        Assert.Equal(CatalogConnectionState.Unused, result.ConnectionState);
        Assert.Null(result.ReadTime);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Supplied);
    }

    [Fact]
    public void MissingConnectionBecomesARecordNotAFailure()
    {
        var builder = ParseCustomer();

        var result = CatalogCompletion.Complete(builder, reader: null);

        Assert.Equal(CatalogConnectionState.NotConfigured, result.ConnectionState);
        Assert.Null(result.ReadTime);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("No database connection"));
    }

    [Fact]
    public void UnreachableCatalogBecomesARecordNotAFailure()
    {
        var builder = ParseCustomer();

        var result = CatalogCompletion.Complete(builder, new UnreachableCatalogReader());

        // A configured but unreachable catalog is infrastructure, not input: the
        // translation continues on conventions, a record says why, and the connection
        // state carries the fact as a field of its own (decisions 015 and 030).
        Assert.Equal(CatalogConnectionState.Unreachable, result.ConnectionState);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("could not be read"));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Supplied);
    }

    private sealed class UnreachableCatalogReader : ICatalogReader
    {
        public IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests)
            => throw new InvalidOperationException("connection refused");

        public IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables)
            => throw new InvalidOperationException("connection refused");
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
                new ColumnImage { Name = "OrderId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
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
                new ColumnImage { Name = "OrderId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
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
        builder.SetPropertyDatabaseType("Total", DatabaseType.Decimal);
        builder.SetPropertyDatabaseMapping("Total", new Dictionary<string, string>
        {
            ["nullable"] = "false",
        });

        CatalogCompletion.Complete(builder, reader: null);

        var total = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Total");
        Assert.Equal(ScalarType.Decimal, total.Property.Type?.ScalarType);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Property == "Total" && r.Reason.Contains("inferred"));
    }
}
