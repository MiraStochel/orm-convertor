using Microsoft.Data.SqlClient;

namespace Tests.Database;

/// <summary>
/// Guards the fixture itself. With Dapper as the source the schema is the expected
/// answer against which F4 measures the share of correctly retrieved metadata
/// (decision 016), so a fixture that quietly stopped containing the cases it promises
/// would make every later verdict about the catalog reader meaningless.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class TestSchemaFixtureTest(TestSchemaFixture fixture)
{
    [Fact]
    public void SchemaExists()
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.schemas WHERE name = @schema";
        command.Parameters.AddWithValue("@schema", fixture.SchemaName);

        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
    }

    [Fact]
    public void AllFixtureTablesExist()
    {
        fixture.SkipIfUnavailable();

        string[] expected =
        [
            "CustomerProfiles", "Customers", "OrderLineAllocations", "OrderLines",
            "Orders", "ProductSuppliers", "Products", "Suppliers"
        ];

        using var connection = fixture.OpenConnection();
        var actual = ReadStrings(connection,
            """
            SELECT t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema
            """,
            ("@schema", fixture.SchemaName));

        // Sorted on this side, not by the server: the ordering of "Products" against
        // "ProductSuppliers" depends on the collation of the instance, and the claim
        // here is about which tables exist, not about how SQL Server sorts them.
        Assert.Equal(expected, actual.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Composite keys of two, three and four parts - the cases F1 and F2 name, and the
    /// ones the model-level tests already cover on the intermediate representation.
    /// </summary>
    [Theory]
    [InlineData("Customers", new[] { "CustomerId" })]
    [InlineData("Orders", new[] { "CompanyId", "OrderId" })]
    [InlineData("OrderLines", new[] { "CompanyId", "OrderId", "LineNo" })]
    [InlineData("OrderLineAllocations", new[] { "CompanyId", "OrderId", "LineNo", "AllocationId" })]
    [InlineData("ProductSuppliers", new[] { "ProductId", "SupplierId" })]
    public void PrimaryKeyHasExpectedPartsInOrder(string table, string[] expectedColumns)
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        Assert.Equal(expectedColumns, ReadPrimaryKeyColumns(connection, table));
    }

    /// <summary>
    /// Multi-column foreign keys, including the ordering of their column pairs - that is
    /// what the resolution phase fills into <c>ColumnPairs</c> and what the catalog has
    /// to deliver for a reference outside the translation unit.
    /// </summary>
    [Theory]
    [InlineData("FK_Orders_Customers", new[] { "CustomerId" }, new[] { "CustomerId" })]
    [InlineData("FK_OrderLines_Orders", new[] { "CompanyId", "OrderId" }, new[] { "CompanyId", "OrderId" })]
    [InlineData("FK_OrderLineAllocations_OrderLines",
        new[] { "CompanyId", "OrderId", "LineNo" }, new[] { "CompanyId", "OrderId", "LineNo" })]
    [InlineData("FK_CustomerProfiles_Customers", new[] { "CustomerId" }, new[] { "CustomerId" })]
    public void ForeignKeyHasExpectedColumnPairs(string constraint, string[] parentColumns, string[] referencedColumns)
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        var pairs = ReadForeignKeyColumns(connection, constraint);

        Assert.Equal(parentColumns, pairs.Select(p => p.Parent));
        Assert.Equal(referencedColumns, pairs.Select(p => p.Referenced));
    }

    /// <summary>
    /// The junction table of decision 005: every part of its primary key is at the same
    /// time a foreign key, and it points at two different tables.
    /// </summary>
    [Fact]
    public void ProductSuppliersIsAJunctionTable()
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();

        var keyColumns = ReadPrimaryKeyColumns(connection, "ProductSuppliers");
        var toProducts = ReadForeignKeyColumns(connection, "FK_ProductSuppliers_Products");
        var toSuppliers = ReadForeignKeyColumns(connection, "FK_ProductSuppliers_Suppliers");

        Assert.Equal(new[] { "ProductId" }, toProducts.Select(p => p.Parent));
        Assert.Equal(new[] { "SupplierId" }, toSuppliers.Select(p => p.Parent));
        Assert.Equal(
            keyColumns.Order(StringComparer.Ordinal),
            toProducts.Concat(toSuppliers).Select(p => p.Parent).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Column facts the catalog reader is supposed to complete: type, nullability,
    /// length and precision/scale.
    /// </summary>
    [Theory]
    [InlineData("Products", "ProductName", "nvarchar", false, 100, null, null)]
    [InlineData("Products", "Sku", "varchar", false, 32, null, null)]
    [InlineData("Products", "UnitPrice", "decimal", false, null, 18, 4)]
    [InlineData("Products", "Weight", "float", true, null, 53, null)]
    [InlineData("Products", "IsDiscontinued", "bit", false, null, null, null)]
    [InlineData("Products", "IntroducedOn", "date", true, null, null, null)]
    [InlineData("Orders", "PlacedAt", "datetime2", false, null, null, null)]
    [InlineData("Orders", "ExternalRef", "uniqueidentifier", true, null, null, null)]
    [InlineData("Customers", "Notes", "nvarchar", true, 400, null, null)]
    public void ColumnCarriesExpectedFacts(
        string table, string column, string dataType, bool isNullable,
        int? maxLength, int? precision, int? scale)
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table AND COLUMN_NAME = @column
            """;
        command.Parameters.AddWithValue("@schema", fixture.SchemaName);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), $"Column {table}.{column} is missing from the fixture.");

        Assert.Equal(dataType, reader.GetString(0));
        Assert.Equal(isNullable ? "YES" : "NO", reader.GetString(1));
        Assert.Equal(maxLength, reader.IsDBNull(2) ? null : Convert.ToInt32(reader.GetValue(2)));
        Assert.Equal(precision, reader.IsDBNull(3) ? null : Convert.ToInt32(reader.GetValue(3)));
        Assert.Equal(scale, reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)));
    }

    /// <summary>
    /// The two single-part keys differ in generation strategy on purpose - one is
    /// IDENTITY, the other assigned by the application (decision 011).
    /// </summary>
    [Theory]
    [InlineData("Customers", "CustomerId", true)]
    [InlineData("Suppliers", "SupplierId", true)]
    [InlineData("Products", "ProductId", false)]
    public void KeyColumnHasExpectedIdentityFlag(string table, string column, bool isIdentity)
    {
        fixture.SkipIfUnavailable();

        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COLUMNPROPERTY(OBJECT_ID(QUOTENAME(@schema) + '.' + QUOTENAME(@table)), @column, 'IsIdentity')";
        command.Parameters.AddWithValue("@schema", fixture.SchemaName);
        command.Parameters.AddWithValue("@table", table);
        command.Parameters.AddWithValue("@column", column);

        Assert.Equal(isIdentity ? 1 : 0, Convert.ToInt32(command.ExecuteScalar()));
    }

    private List<string> ReadPrimaryKeyColumns(SqlConnection connection, string table) =>
        ReadStrings(connection,
            """
            SELECT c.name
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE i.is_primary_key = 1 AND s.name = @schema AND t.name = @table
            ORDER BY ic.key_ordinal
            """,
            ("@schema", fixture.SchemaName), ("@table", table));

    private List<(string Parent, string Referenced)> ReadForeignKeyColumns(SqlConnection connection, string constraint)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT pc.name, rc.name
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            WHERE fk.name = @constraint AND SCHEMA_NAME(fk.schema_id) = @schema
            ORDER BY fkc.constraint_column_id
            """;
        command.Parameters.AddWithValue("@constraint", constraint);
        command.Parameters.AddWithValue("@schema", fixture.SchemaName);

        var pairs = new List<(string, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            pairs.Add((reader.GetString(0), reader.GetString(1)));
        }

        Assert.NotEmpty(pairs);
        return pairs;
    }

    private static List<string> ReadStrings(
        SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
