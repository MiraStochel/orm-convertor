using Common.Naming;
using DatabaseCatalog;
using Model.AbstractRepresentation.Enums;
using Tests.Database;

namespace Tests.Catalog;

/// <summary>
/// The mechanism side of decision 015 against the schema the tests own (decision 016):
/// the reader resolves tables from entity-name candidates and returns the column image,
/// key order and foreign key pairs the schema script declares. The script is the expected
/// answer, which is what makes the share of correctly retrieved metadata of F4 measurable.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class SqlServerCatalogReaderTest(TestSchemaFixture fixture)
{
    private IReadOnlyDictionary<string, TableLookup> Read(params TableRequest[] requests)
    {
        using var reader = new SqlServerCatalogReader(TestDatabase.ConnectionString!);
        return reader.ReadTables(requests);
    }

    private TableImage ImageOf(string entityName)
    {
        var lookups = Read(new TableRequest(entityName, null, EntityTableNaming.TableCandidatesFor(entityName)));
        var image = lookups[entityName].Image;
        Assert.NotNull(image);
        return image;
    }

    [Fact]
    public void ResolvesATableFromTheSingularEntityName()
    {
        fixture.SkipIfUnavailable();

        // Entity "Customer", table "Customers": the plural candidate resolves it, and the
        // schema comes back with the table - the very fact a Dapper source cannot state.
        var image = ImageOf("Customer");

        Assert.Equal("Customers", image.Name);
        Assert.Equal(fixture.SchemaName, image.Schema);
    }

    [Fact]
    public void ReadsColumnTypesLengthsAndNullability()
    {
        fixture.SkipIfUnavailable();

        var products = ImageOf("Product");

        var sku = products.FindColumn("Sku");
        Assert.NotNull(sku);
        Assert.Equal(DatabaseType.VarChar, sku.Type);
        Assert.False(sku.IsUnicode);
        Assert.Equal(32, sku.Length);
        Assert.False(sku.IsNullable);

        var name = products.FindColumn("ProductName");
        Assert.NotNull(name);
        Assert.Equal(DatabaseType.VarChar, name.Type);
        Assert.True(name.IsUnicode);
        // nvarchar length is characters, not the bytes sys.columns counts.
        Assert.Equal(100, name.Length);

        var price = products.FindColumn("UnitPrice");
        Assert.NotNull(price);
        Assert.Equal(DatabaseType.Decimal, price.Type);
        Assert.Equal(18, price.Precision);
        Assert.Equal(4, price.Scale);

        var weight = products.FindColumn("Weight");
        Assert.NotNull(weight);
        Assert.Equal(DatabaseType.DoublePrecision, weight.Type);
        Assert.True(weight.IsNullable);

        // NVARCHAR(MAX) carries no length: -1 is a marker, not a fact.
        var description = products.FindColumn("Description");
        Assert.NotNull(description);
        Assert.Null(description.Length);
    }

    [Fact]
    public void ReadsTheFractionalSecondPrecisionOfADateTimeColumn()
    {
        fixture.SkipIfUnavailable();

        var placedAt = ImageOf("Order").FindColumn("PlacedAt");

        Assert.NotNull(placedAt);
        Assert.Equal(DatabaseType.Timestamp, placedAt.Type);
        Assert.Equal(3, placedAt.Precision);
    }

    [Fact]
    public void ReadsIdentityAndTheFourPartKeyInOrder()
    {
        fixture.SkipIfUnavailable();

        var customers = ImageOf("Customer");
        Assert.True(customers.FindColumn("CustomerId")!.IsIdentity);
        Assert.Equal(new[] { "CustomerId" }, customers.PrimaryKeyColumns);

        // Products' key is assigned by the application - no identity.
        Assert.False(ImageOf("Product").FindColumn("ProductId")!.IsIdentity);

        Assert.Equal(
            new[] { "CompanyId", "OrderId", "LineNo", "AllocationId" },
            ImageOf("OrderLineAllocation").PrimaryKeyColumns);
    }

    [Fact]
    public void ReadsUniqueConstraintsAndKeepsThePrimaryKeyOutOfThem()
    {
        fixture.SkipIfUnavailable();

        // F4 names unique constraints among the metadata to be read (decision 055).
        var products = ImageOf("Product");

        var sku = Assert.Single(products.UniqueConstraints);
        Assert.Equal("UQ_Products_Sku", sku.Name);
        Assert.Equal(new[] { "Sku" }, sku.Columns);

        // The primary key is a unique index too, and reading it here would state the same
        // fact twice under two categories.
        Assert.DoesNotContain(products.UniqueConstraints, c => c.Columns.Contains("ProductId"));

        // Several columns come back as one constraint, in the order the constraint declares.
        var composite = Assert.Single(ImageOf("ProductSupplier").UniqueConstraints);
        Assert.Equal("UQ_ProductSuppliers_SupplierSku", composite.Name);
        Assert.Equal(new[] { "SupplierId", "SupplierSku" }, composite.Columns);

        // A table declaring none says so with an empty list, not with a missing image.
        Assert.Empty(ImageOf("Customer").UniqueConstraints);
    }

    [Fact]
    public void ReadsMultiColumnForeignKeysWithTheirPairs()
    {
        fixture.SkipIfUnavailable();

        var orderLines = ImageOf("OrderLine");
        Assert.Equal(2, orderLines.ForeignKeys.Count);

        var toOrders = Assert.Single(orderLines.ForeignKeys, fk => fk.ReferencedTable == "Orders");
        Assert.Equal(fixture.SchemaName, toOrders.ReferencedSchema);
        Assert.Equal(
            new[] { new ForeignKeyColumn("CompanyId", "CompanyId"), new ForeignKeyColumn("OrderId", "OrderId") },
            toOrders.Columns);

        var threeColumn = Assert.Single(ImageOf("OrderLineAllocation").ForeignKeys);
        Assert.Equal("OrderLines", threeColumn.ReferencedTable);
        Assert.Equal(3, threeColumn.Columns.Count);
    }

    [Fact]
    public void FindsTheJunctionTableBetweenProductsAndSuppliers()
    {
        fixture.SkipIfUnavailable();

        using var reader = new SqlServerCatalogReader(TestDatabase.ConnectionString!);
        var junctions = reader.FindJunctionTables([ImageOf("Product"), ImageOf("Supplier")]);

        // ProductSuppliers is the junction of the schema: its whole key is two foreign
        // keys, and the payload column does not disqualify it.
        var junction = Assert.Single(junctions);
        Assert.Equal("ProductSuppliers", junction.Name);
        Assert.Equal(2, junction.ForeignKeys.Count);
        Assert.NotNull(junction.FindColumn("SupplierSku"));
    }

    [Fact]
    public void OrdinaryReferencingTablesAreNotJunctions()
    {
        fixture.SkipIfUnavailable();

        // Orders references Customers without being key-covered, CustomerProfiles covers
        // its key with a single foreign key - neither is the two-key junction shape.
        using var reader = new SqlServerCatalogReader(TestDatabase.ConnectionString!);
        var junctions = reader.FindJunctionTables([ImageOf("Customer")]);

        Assert.Empty(junctions);
    }

    [Fact]
    public void AnUnknownNameResolvesToNothing()
    {
        fixture.SkipIfUnavailable();

        var lookups = Read(new TableRequest("Nothing", null, EntityTableNaming.TableCandidatesFor("Nothing")));

        Assert.Null(lookups["Nothing"].Image);
        Assert.Empty(lookups["Nothing"].AmbiguousMatches);
    }

    [Fact]
    public void AStatedSchemaConstrainsTheMatch()
    {
        fixture.SkipIfUnavailable();

        var lookups = Read(
            new TableRequest("Right", fixture.SchemaName, ["Customers"]),
            new TableRequest("Wrong", "no_such_schema", ["Customers"]));

        Assert.NotNull(lookups["Right"].Image);
        Assert.Null(lookups["Wrong"].Image);
    }
}
