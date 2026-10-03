using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Tests.LdbcJudge;

/// <summary>
/// The inserts INS 1-8 of the validation set and their compensation (decision 117), run from
/// the scripts in <c>database/ldbc/updates</c>, which this assembly embeds and the Java suite
/// reads as test resources - one copy of each. An insert is DML and not a translation: the
/// script knows the operation, and this class binds every field of the operation to the
/// parameter of the same name and knows nothing else about it - a number as BIGINT, a text as
/// NVARCHAR, a list or an object as its JSON text, which the script reads with OPENJSON.
/// </summary>
internal static class LdbcUpdates
{
    private const string ResourcePrefix = "Tests.Database.Ldbc.updates.";
    private const string SchemaPlaceholder = "{{schema}}";

    /// <summary>A replay over SF 1 inserts thousands of rows and deletes them again; no step may time out at the default 30 s.</summary>
    private const int CommandTimeoutSeconds = 600;

    /// <summary>Runs the script of one insert - INS1 to INS8 - with the fields of the operation as its parameters.</summary>
    public static void Insert(SqlConnection connection, string operation, string parameters)
    {
        using var document = JsonDocument.Parse(parameters);
        using var command = connection.CreateCommand();
        command.CommandText = Script(operation.ToLowerInvariant() + ".sql");
        command.CommandTimeout = CommandTimeoutSeconds;

        foreach (var field in document.RootElement.EnumerateObject())
        {
            command.Parameters.Add(Parameter(field.Name, field.Value));
        }

        command.ExecuteNonQuery();
    }

    /// <summary>Deletes every row the inserts of the set write, however many of them ran (<c>undo.sql</c>).</summary>
    public static void Undo(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Script("undo.sql");
        command.CommandTimeout = CommandTimeoutSeconds;
        command.ExecuteNonQuery();
    }

    /// <summary>The text of one script, pointed at the schema LdbcSnb holds the tables in.</summary>
    public static string Script(string name)
    {
        using var stream = typeof(LdbcUpdates).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException(
                $"The script \"{name}\" is missing: Tests.csproj embeds database/ldbc/updates/*.sql.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd().Replace(SchemaPlaceholder, LdbcDatabase.Schema, StringComparison.Ordinal);
    }

    private static SqlParameter Parameter(string name, JsonElement value)
    {
        var parameter = new SqlParameter { ParameterName = "@" + name };

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out var integer):
                parameter.SqlDbType = SqlDbType.BigInt;
                parameter.Value = integer;
                break;
            case JsonValueKind.Number:
                parameter.SqlDbType = SqlDbType.Float;
                parameter.Value = value.GetDouble();
                break;
            case JsonValueKind.String:
                parameter.SqlDbType = SqlDbType.NVarChar;
                parameter.Size = -1;
                parameter.Value = value.GetString();
                break;
            case JsonValueKind.True or JsonValueKind.False:
                parameter.SqlDbType = SqlDbType.Bit;
                parameter.Value = value.GetBoolean();
                break;
            case JsonValueKind.Array or JsonValueKind.Object:
                parameter.SqlDbType = SqlDbType.NVarChar;
                parameter.Size = -1;
                parameter.Value = value.GetRawText();
                break;
            default:
                parameter.SqlDbType = SqlDbType.NVarChar;
                parameter.Size = -1;
                parameter.Value = DBNull.Value;
                break;
        }

        return parameter;
    }
}
