using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Tests.Database;

/// <summary>
/// Creates the schema described by <c>TestSchema.sql</c> before the database collection
/// runs and drops it afterwards (decision 016). What decision 016 left to implementation
/// is settled here:
/// <list type="bullet">
/// <item>the schema is described by an SQL script, not by code, so that it can also be
/// run by hand against the local instance and read as the expected answer it is;</item>
/// <item>it is created once per collection, not per test - the catalog reader reads
/// metadata, and metadata is written once and read many times;</item>
/// <item>the fixture itself runs no transaction: DDL is shared and read-only for the
/// second and third verification level. A fourth-level test that stores rows opens its
/// own transaction on <see cref="OpenConnection"/> and rolls it back, so that no test
/// depends on what another one left behind;</item>
/// <item>a run left over from a crashed previous run is dropped before the schema is
/// created, so a failed cleanup never blocks the next run.</item>
/// </list>
/// The fixture never fails on infrastructure: when there is no database, or the
/// configured one cannot be reached, it records the reason. Whether the tests then skip
/// with that reason or fail with it is decided by the environment (decision 038).
/// </summary>
public sealed class TestSchemaFixture : IAsyncLifetime
{
    private const string ScriptResourceName = "Tests.Database.TestSchema.sql";
    private const string SchemaPlaceholder = "{{schema}}";

    /// <summary>
    /// Drops everything the fixture may have created. Written against the catalog rather
    /// than against a list of table names, so that adding a table to the script does not
    /// silently leave a leftover behind.
    /// </summary>
    private const string DropSchemaSql =
        """
        DECLARE @sql NVARCHAR(MAX) = N'';

        SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
                           + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema;

        SELECT @sql = @sql + N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
        FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE s.name = @schema;

        IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schema)
            SET @sql = @sql + N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';

        IF LEN(@sql) > 0
            EXEC sp_executesql @sql;
        """;

    /// <summary>Schema the fixture owns.</summary>
    public string SchemaName => TestDatabase.SchemaName;

    /// <summary>Whether the schema really stands and tests may talk to it.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>Why it does not, when it does not. Empty while <see cref="IsAvailable"/>.</summary>
    public string SkipReason { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        if (!TestDatabase.IsConfigured)
        {
            SkipReason = TestDatabase.NotConfiguredReason;
            return;
        }

        try
        {
            await using var connection = new SqlConnection(TestDatabase.ConnectionString);
            await connection.OpenAsync();

            await DropSchemaAsync(connection);

            foreach (var batch in ReadScriptBatches())
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }

            IsAvailable = true;
        }
        catch (Exception ex)
        {
            // A configured but unreachable database skips as well, and says so - the
            // suite must not go red because of infrastructure (decision 016). The reason
            // distinguishes it from "nothing configured", so a broken local instance is
            // not mistaken for an absent one.
            IsAvailable = false;
            SkipReason =
                $"Test database \"{TestDatabase.ConnectionStringName}\" is configured but the schema "
                + $"\"{SchemaName}\" could not be prepared: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!IsAvailable)
        {
            return;
        }

        try
        {
            await using var connection = new SqlConnection(TestDatabase.ConnectionString);
            await connection.OpenAsync();
            await DropSchemaAsync(connection);
        }
        catch (Exception)
        {
            // Cleanup that fails must not turn a green run red; the next run drops
            // whatever is left before it creates the schema again.
        }
    }

    /// <summary>
    /// Opens a connection to the test database. Callers are responsible for their own
    /// data: a test that writes rows wraps them in a transaction and rolls it back.
    /// </summary>
    public SqlConnection OpenConnection()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(SkipReason);
        }

        var connection = new SqlConnection(TestDatabase.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Skips the current test with the recorded reason when the database is not there -
    /// unless the environment stated that it provides one (decision 038), in which case
    /// the same reason is reported as a failure instead. Call it as the first statement
    /// of every database-dependent test.
    /// </summary>
    public void SkipIfUnavailable()
    {
        if (IsAvailable)
        {
            return;
        }

        if (TestDatabase.IsRequired)
        {
            Assert.Fail(TestDatabase.RequiredButMissingReason(SkipReason));
        }
        else
        {
            Assert.Skip(SkipReason);
        }
    }

    private async Task DropSchemaAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = DropSchemaSql;
        command.Parameters.AddWithValue("@schema", SchemaName);
        await command.ExecuteNonQueryAsync();
    }

    private IEnumerable<string> ReadScriptBatches()
    {
        using var stream = typeof(TestSchemaFixture).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource \"{ScriptResourceName}\" is missing from the test assembly.");
        using var reader = new StreamReader(stream);
        var script = reader.ReadToEnd().Replace(SchemaPlaceholder, SchemaName, StringComparison.Ordinal);

        // CREATE SCHEMA has to start its own batch, so the script is split on GO - which
        // is a client-side separator, not a T-SQL statement, and SqlCommand does not know it.
        return Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0)
            .ToList();
    }
}
