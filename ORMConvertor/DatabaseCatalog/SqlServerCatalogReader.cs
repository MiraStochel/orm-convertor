using Microsoft.Data.SqlClient;
using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// Reads the SQL Server catalog. The whole batch of requests is served by one connection
/// and three queries - columns, primary keys, foreign keys - regardless of how many tables
/// take part, so the phase stays bounded and measurable (S3) instead of turning into one
/// query per missing fact. The sys.* views are used rather than INFORMATION_SCHEMA because
/// identity and key ordering live only there.
/// </summary>
public sealed class SqlServerCatalogReader(string connectionString) : ICatalogReader
{
    public IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests)
    {
        var results = new Dictionary<string, TableLookup>();

        var names = requests
            .SelectMany(r => r.NameCandidates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (names.Count == 0)
        {
            return results;
        }

        using var connection = new SqlConnection(connectionString);
        connection.Open();

        var images = LoadImages(connection, names);

        foreach (var request in requests)
        {
            results[request.Key] = Resolve(request, images);
        }

        return results;
    }

    private static TableLookup Resolve(TableRequest request, IReadOnlyList<TableImage> images)
    {
        foreach (var candidate in request.NameCandidates)
        {
            var matches = images
                .Where(image => string.Equals(image.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    && (request.Schema is null
                        || string.Equals(image.Schema, request.Schema, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 1)
            {
                return new TableLookup { Image = matches[0] };
            }

            if (matches.Count > 1)
            {
                // The same table name in several schemas. dbo is preferred, matching what
                // the harness always did; anything else is ambiguous and reported rather
                // than guessed.
                var dbo = matches.FirstOrDefault(m => string.Equals(m.Schema, "dbo", StringComparison.OrdinalIgnoreCase));

                return dbo is not null
                    ? new TableLookup { Image = dbo }
                    : new TableLookup { AmbiguousMatches = [.. matches.Select(m => m.QualifiedName)] };
            }
        }

        return new TableLookup();
    }

    private static List<TableImage> LoadImages(SqlConnection connection, IReadOnlyList<string> names)
    {
        var columns = new Dictionary<(string Schema, string Table), List<ColumnImage>>();
        var keys = new Dictionary<(string Schema, string Table), List<string>>();
        var foreignKeys = new Dictionary<(string Schema, string Table), List<ForeignKeyImage>>();

        var inClause = string.Join(", ", Enumerable.Range(0, names.Count).Select(i => $"@n{i}"));

        void AddNameParameters(SqlCommand command)
        {
            for (var i = 0; i < names.Count; i++)
            {
                command.Parameters.AddWithValue($"@n{i}", names[i]);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale,
                       c.is_nullable, c.is_identity
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                JOIN sys.columns c ON c.object_id = t.object_id
                JOIN sys.types ty ON ty.user_type_id = c.user_type_id
                WHERE t.name IN ({inClause})
                ORDER BY s.name, t.name, c.column_id
                """;
            AddNameParameters(command);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!columns.TryGetValue(key, out var list))
                {
                    columns[key] = list = [];
                }

                list.Add(ReadColumn(
                    name: reader.GetString(2),
                    sqlType: reader.GetString(3),
                    maxLength: reader.GetInt16(4),
                    precision: reader.GetByte(5),
                    scale: reader.GetByte(6),
                    isNullable: reader.GetBoolean(7),
                    isIdentity: reader.GetBoolean(8)));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT s.name, t.name, col.name
                FROM sys.key_constraints kc
                JOIN sys.tables t ON t.object_id = kc.parent_object_id
                JOIN sys.schemas s ON s.schema_id = t.schema_id
                JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
                JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                WHERE kc.type = 'PK' AND t.name IN ({inClause})
                ORDER BY s.name, t.name, ic.key_ordinal
                """;
            AddNameParameters(command);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetString(1));
                if (!keys.TryGetValue(key, out var list))
                {
                    keys[key] = list = [];
                }

                list.Add(reader.GetString(2));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT ps.name, pt.name, fk.name, pc.name, rs.name, rt.name, rc.name
                FROM sys.foreign_keys fk
                JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
                JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
                JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
                JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
                JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                WHERE pt.name IN ({inClause})
                ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id
                """;
            AddNameParameters(command);

            using var reader = command.ExecuteReader();
            (string Schema, string Table, string Fk)? current = null;
            List<ForeignKeyColumn> pairs = [];
            string referencedSchema = string.Empty, referencedTable = string.Empty;

            void Flush()
            {
                if (current is not { } c)
                {
                    return;
                }

                if (!foreignKeys.TryGetValue((c.Schema, c.Table), out var list))
                {
                    foreignKeys[(c.Schema, c.Table)] = list = [];
                }

                list.Add(new ForeignKeyImage
                {
                    Name = c.Fk,
                    ReferencedSchema = referencedSchema,
                    ReferencedTable = referencedTable,
                    Columns = pairs,
                });
                pairs = [];
            }

            while (reader.Read())
            {
                var row = (Schema: reader.GetString(0), Table: reader.GetString(1), Fk: reader.GetString(2));
                if (current != row)
                {
                    Flush();
                    current = row;
                }

                referencedSchema = reader.GetString(4);
                referencedTable = reader.GetString(5);
                pairs.Add(new ForeignKeyColumn(reader.GetString(3), reader.GetString(6)));
            }

            Flush();
        }

        return [.. columns.Select(entry => new TableImage
        {
            Schema = entry.Key.Schema,
            Name = entry.Key.Table,
            Columns = entry.Value,
            PrimaryKeyColumns = keys.TryGetValue(entry.Key, out var pk) ? pk : [],
            ForeignKeys = foreignKeys.TryGetValue(entry.Key, out var fks) ? fks : [],
        })];
    }

    private static ColumnImage ReadColumn(
        string name, string sqlType, short maxLength, byte precision, byte scale, bool isNullable, bool isIdentity)
    {
        var type = MapType(sqlType);

        // Only the values meaningful for the type are carried; sys.columns states a
        // precision for every numeric column and a byte length for every column, which
        // as model facts would be noise rather than information.
        int? length = type switch
        {
            DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Binary or DatabaseType.VarBinary
                => maxLength > 0 ? maxLength : null,
            DatabaseType.NChar or DatabaseType.NVarChar
                => maxLength > 0 ? maxLength / 2 : null,
            _ => null,
        };

        (int? columnPrecision, int? columnScale) = type switch
        {
            DatabaseType.Decimal or DatabaseType.Numeric => ((int?)precision, (int?)scale),
            // For date-time columns the fractional-second precision is what mappings
            // express as precision; sys.columns keeps it in scale.
            DatabaseType.DateTime2 or DatabaseType.DateTimeOffset or DatabaseType.Time => ((int?)scale, (int?)null),
            _ => ((int?)null, (int?)null),
        };

        return new ColumnImage
        {
            Name = name,
            Type = type,
            Length = length,
            Precision = columnPrecision,
            Scale = columnScale,
            IsNullable = isNullable,
            IsIdentity = isIdentity,
        };
    }

    /// <summary>
    /// The T-SQL name of a type as the catalog spells it. A name outside the vocabulary
    /// maps to null: the fact is not supplied rather than guessed. This is the catalog's
    /// side of the DatabaseType list being a T-SQL vocabulary today - the open item on
    /// database type neutralization owns the wider question.
    /// </summary>
    private static DatabaseType? MapType(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "bigint" => DatabaseType.BigInt,
        "int" => DatabaseType.Int,
        "smallint" => DatabaseType.SmallInt,
        "tinyint" => DatabaseType.TinyInt,
        "bit" => DatabaseType.Bit,
        "decimal" => DatabaseType.Decimal,
        "numeric" => DatabaseType.Numeric,
        "money" => DatabaseType.Money,
        "smallmoney" => DatabaseType.SmallMoney,
        "float" => DatabaseType.Float,
        "real" => DatabaseType.Real,
        "date" => DatabaseType.Date,
        "datetime" => DatabaseType.DateTime,
        "datetime2" => DatabaseType.DateTime2,
        "smalldatetime" => DatabaseType.SmallDateTime,
        "time" => DatabaseType.Time,
        "datetimeoffset" => DatabaseType.DateTimeOffset,
        "char" => DatabaseType.Char,
        "varchar" => DatabaseType.VarChar,
        "text" => DatabaseType.Text,
        "nchar" => DatabaseType.NChar,
        "nvarchar" or "sysname" => DatabaseType.NVarChar,
        "ntext" => DatabaseType.NText,
        "binary" => DatabaseType.Binary,
        "varbinary" => DatabaseType.VarBinary,
        "image" => DatabaseType.Image,
        "uniqueidentifier" => DatabaseType.UniqueIdentifier,
        "xml" => DatabaseType.Xml,
        "sql_variant" => DatabaseType.SqlVariant,
        "rowversion" or "timestamp" => DatabaseType.RowVersion,
        _ => null,
    };
}
