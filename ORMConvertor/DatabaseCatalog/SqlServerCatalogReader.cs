using System.Data;
using Microsoft.Data.SqlClient;
using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// Reads the SQL Server catalog. The whole batch of requests is served by one connection
/// and four queries - columns, primary keys, foreign keys, unique constraints - regardless
/// of how many tables take part, so the phase stays bounded and measurable (S3) instead of
/// turning into one query per missing fact. The sys.* views are used rather than
/// INFORMATION_SCHEMA because identity and key ordering live only there.
/// The one connection is opened lazily and lives with the reader instance, so a completion
/// phase - table images first, junction probe after - rents it from the pool once; dispose
/// the reader to give it back.
/// </summary>
public sealed class SqlServerCatalogReader(string connectionString) : ICatalogReader, IDisposable
{
    private SqlConnection? connection;

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

        var images = LoadImages(Open(), names);

        foreach (var request in requests)
        {
            results[request.Key] = Resolve(request, images);
        }

        return results;
    }

    public IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables)
    {
        if (referencedTables.Count == 0)
        {
            return [];
        }

        var connection = Open();

        var referencedNames = referencedTables.Select(t => t.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var referenced = referencedTables
            .Select(t => (t.Schema.ToLowerInvariant(), t.Name.ToLowerInvariant()))
            .ToHashSet();

        // Tables holding a foreign key towards any of the given tables. The given tables
        // themselves are dropped right here: the caller already holds their images, so
        // re-reading them would only repeat an answer it has.
        var candidateNames = new List<string>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT DISTINCT ps.name, pt.name
                FROM sys.foreign_keys fk
                JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
                JOIN sys.schemas ps ON ps.schema_id = pt.schema_id
                JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
                WHERE rt.name IN ({InClause(referencedNames)})
                """;
            AddNameParameters(command, referencedNames);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var schema = reader.GetString(0);
                var name = reader.GetString(1);

                if (!referenced.Contains((schema.ToLowerInvariant(), name.ToLowerInvariant())))
                {
                    candidateNames.Add(name);
                }
            }
        }

        candidateNames = candidateNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (candidateNames.Count == 0)
        {
            return [];
        }

        // The junction shape is a question about keys alone, so only the key halves of the
        // image are read over the candidates; the column image - the widest of the four
        // queries - is loaded afterwards for the few tables that survive the shape test.
        var keys = LoadPrimaryKeys(connection, candidateNames);
        var foreignKeys = LoadForeignKeys(connection, candidateNames);

        var junctions = keys.Keys
            .Where(table => !referenced.Contains((table.Schema.ToLowerInvariant(), table.Table.ToLowerInvariant())))
            .Select(table => new TableImage
            {
                Schema = table.Schema,
                Name = table.Table,
                Columns = [],
                PrimaryKeyColumns = keys[table],
                ForeignKeys = foreignKeys.TryGetValue(table, out var fks) ? fks : [],
            })
            .Where(image => JunctionShape.TryGet(image) is { } shape
                && References(shape.First, referenced)
                && References(shape.Second, referenced))
            .ToList();

        if (junctions.Count == 0)
        {
            return [];
        }

        var columns = LoadColumns(
            connection,
            junctions.Select(j => j.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        return [.. junctions.Select(junction => new TableImage
        {
            Schema = junction.Schema,
            Name = junction.Name,
            Columns = columns.TryGetValue((junction.Schema, junction.Name), out var c) ? c : [],
            PrimaryKeyColumns = junction.PrimaryKeyColumns,
            ForeignKeys = junction.ForeignKeys,
        })];
    }

    public void Dispose() => connection?.Dispose();

    /// <summary>
    /// The reader's one connection, opened on first use. A connection the pool or the
    /// server dropped in the meantime is reopened rather than reported - the reader
    /// claims nothing about liveness between two calls.
    /// </summary>
    private SqlConnection Open()
    {
        connection ??= new SqlConnection(connectionString);

        if (connection.State == ConnectionState.Broken)
        {
            connection.Close();
        }

        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        return connection;
    }

    private static bool References(ForeignKeyImage fk, HashSet<(string, string)> referenced)
        => referenced.Contains((fk.ReferencedSchema.ToLowerInvariant(), fk.ReferencedTable.ToLowerInvariant()));

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
        var columns = LoadColumns(connection, names);
        var keys = LoadPrimaryKeys(connection, names);
        var uniques = LoadUniqueConstraints(connection, names);
        var foreignKeys = LoadForeignKeys(connection, names);

        return [.. columns.Select(entry => new TableImage
        {
            Schema = entry.Key.Schema,
            Name = entry.Key.Table,
            Columns = entry.Value,
            PrimaryKeyColumns = keys.TryGetValue(entry.Key, out var pk) ? pk : [],
            ForeignKeys = foreignKeys.TryGetValue(entry.Key, out var fks) ? fks : [],
            UniqueConstraints = uniques.TryGetValue(entry.Key, out var uqs) ? uqs : [],
        })];
    }

    private static Dictionary<(string Schema, string Table), List<ColumnImage>> LoadColumns(
        SqlConnection connection, IReadOnlyList<string> names)
    {
        var columns = new Dictionary<(string Schema, string Table), List<ColumnImage>>();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.name, t.name, c.name, ty.name, c.max_length, c.precision, c.scale,
                   c.is_nullable, c.is_identity
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.columns c ON c.object_id = t.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.name IN ({InClause(names)})
            ORDER BY s.name, t.name, c.column_id
            """;
        AddNameParameters(command, names);

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

        return columns;
    }

    private static Dictionary<(string Schema, string Table), List<string>> LoadPrimaryKeys(
        SqlConnection connection, IReadOnlyList<string> names)
    {
        var keys = new Dictionary<(string Schema, string Table), List<string>>();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.name, t.name, col.name
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE kc.type = 'PK' AND t.name IN ({InClause(names)})
            ORDER BY s.name, t.name, ic.key_ordinal
            """;
        AddNameParameters(command, names);

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

        return keys;
    }

    /// <summary>
    /// Unique constraints (decision 055). The same view as the primary key with the
    /// other type, so the shape of the query and the ordering rule are identical; only
    /// here several constraints share a table, which is why the name is read too and
    /// the ordering is by name before column, so that a constraint's columns arrive in
    /// one uninterrupted run.
    /// </summary>
    private static Dictionary<(string Schema, string Table), List<UniqueConstraintImage>> LoadUniqueConstraints(
        SqlConnection connection, IReadOnlyList<string> names)
    {
        var uniques = new Dictionary<(string Schema, string Table), List<UniqueConstraintImage>>();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.name, t.name, kc.name, col.name
            FROM sys.key_constraints kc
            JOIN sys.tables t ON t.object_id = kc.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            WHERE kc.type = 'UQ' AND t.name IN ({InClause(names)})
            ORDER BY s.name, t.name, kc.name, ic.key_ordinal
            """;
        AddNameParameters(command, names);

        using var reader = command.ExecuteReader();
        (string Schema, string Table, string Name)? current = null;
        List<string> constraintColumns = [];

        void Flush()
        {
            if (current is not { } c)
            {
                return;
            }

            if (!uniques.TryGetValue((c.Schema, c.Table), out var list))
            {
                uniques[(c.Schema, c.Table)] = list = [];
            }

            list.Add(new UniqueConstraintImage { Name = c.Name, Columns = constraintColumns });
            constraintColumns = [];
        }

        while (reader.Read())
        {
            var row = (Schema: reader.GetString(0), Table: reader.GetString(1), Name: reader.GetString(2));
            if (current != row)
            {
                Flush();
                current = row;
            }

            constraintColumns.Add(reader.GetString(3));
        }

        Flush();

        return uniques;
    }

    private static Dictionary<(string Schema, string Table), List<ForeignKeyImage>> LoadForeignKeys(
        SqlConnection connection, IReadOnlyList<string> names)
    {
        var foreignKeys = new Dictionary<(string Schema, string Table), List<ForeignKeyImage>>();

        using var command = connection.CreateCommand();
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
            WHERE pt.name IN ({InClause(names)})
            ORDER BY ps.name, pt.name, fk.name, fkc.constraint_column_id
            """;
        AddNameParameters(command, names);

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

        return foreignKeys;
    }

    private static string InClause(IReadOnlyList<string> names)
        => string.Join(", ", Enumerable.Range(0, names.Count).Select(i => $"@n{i}"));

    private static void AddNameParameters(SqlCommand command, IReadOnlyList<string> names)
    {
        for (var i = 0; i < names.Count; i++)
        {
            // Declared as sysname explicitly: AddWithValue would size each parameter to
            // its value, and every distinct length would be its own plan cache entry.
            var parameter = command.Parameters.Add($"@n{i}", SqlDbType.NVarChar, 128);
            parameter.Value = names[i];
        }
    }

    private static ColumnImage ReadColumn(
        string name, string sqlType, short maxLength, byte precision, byte scale, bool isNullable, bool isIdentity)
    {
        var (type, isUnicode, keepLiteral) = MapType(sqlType);

        // Only the values meaningful for the type are carried; sys.columns states a
        // precision for every numeric column and a byte length for every column, which
        // as model facts would be noise rather than information. Unicode character
        // columns state their byte length, so the character count is half of it.
        int? length = type switch
        {
            DatabaseType.Char or DatabaseType.VarChar when isUnicode == true
                => maxLength > 0 ? maxLength / 2 : null,
            DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Binary or DatabaseType.VarBinary
                => maxLength > 0 ? maxLength : null,
            _ => null,
        };

        (int? columnPrecision, int? columnScale) = type switch
        {
            DatabaseType.Decimal => ((int?)precision, (int?)scale),
            // For date-time columns the fractional-second precision is what mappings
            // express as precision; sys.columns keeps it in scale. The Timestamp family
            // covers datetime and smalldatetime too, whose fixed precisions the catalog
            // states the same way (decision 019).
            DatabaseType.Timestamp or DatabaseType.TimestampWithTimeZone or DatabaseType.Time
                => ((int?)scale, (int?)null),
            _ => ((int?)null, (int?)null),
        };

        return new ColumnImage
        {
            Name = name,
            Type = type,
            IsUnicode = isUnicode,
            SourceSqlType = keepLiteral ? sqlType : null,
            Length = length,
            Precision = columnPrecision,
            Scale = columnScale,
            IsNullable = isNullable,
            IsIdentity = isIdentity,
            // In SQL Server a rowversion column carries the row version and nothing else,
            // so the type name alone states the versioning claim (decision 030); timestamp
            // is the same type's deprecated spelling.
            IsRowVersion = sqlType.Equals("rowversion", StringComparison.OrdinalIgnoreCase)
                || sqlType.Equals("timestamp", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// The T-SQL name of a type as the catalog spells it, read into the neutral vocabulary
    /// (decision 019): the family, the unicode facet of character types, and whether the
    /// literal spelling belongs on the escape path because the family is coarser than the
    /// type - money, datetime, rowversion. A name outside the vocabulary has no family and
    /// keeps only its spelling: the fact is carried literally rather than guessed.
    /// </summary>
    private static (DatabaseType? Type, bool? IsUnicode, bool KeepLiteral) MapType(string sqlType)
        => sqlType.ToLowerInvariant() switch
        {
            "bit" => (DatabaseType.Boolean, null, false),
            "tinyint" => (DatabaseType.TinyInt, null, false),
            "smallint" => (DatabaseType.SmallInt, null, false),
            "int" => (DatabaseType.Integer, null, false),
            "bigint" => (DatabaseType.BigInt, null, false),

            "decimal" or "numeric" => (DatabaseType.Decimal, null, false),
            // The money types keep their exact decimal shape through sys.columns'
            // precision and scale; the literal records their narrower range.
            "money" or "smallmoney" => (DatabaseType.Decimal, null, true),

            "float" => (DatabaseType.DoublePrecision, null, false),
            "real" => (DatabaseType.Real, null, false),

            "date" => (DatabaseType.Date, null, false),
            "time" => (DatabaseType.Time, null, false),
            "datetime" or "smalldatetime" => (DatabaseType.Timestamp, null, true),
            "datetime2" => (DatabaseType.Timestamp, null, false),
            "datetimeoffset" => (DatabaseType.TimestampWithTimeZone, null, false),

            "char" => (DatabaseType.Char, false, false),
            "nchar" => (DatabaseType.Char, true, false),
            "varchar" => (DatabaseType.VarChar, false, false),
            "nvarchar" or "sysname" => (DatabaseType.VarChar, true, false),
            "text" => (DatabaseType.Text, false, false),
            "ntext" => (DatabaseType.Text, true, false),

            "binary" => (DatabaseType.Binary, null, false),
            "varbinary" => (DatabaseType.VarBinary, null, false),
            "image" => (DatabaseType.Blob, null, false),

            "uniqueidentifier" => (DatabaseType.Uuid, null, false),
            "xml" => (DatabaseType.Xml, null, false),

            // A rowversion column is eight bytes of binary; the versioning claim is a
            // mapping fact of its own, stated through IsRowVersion (decision 030), and
            // the literal keeps the exact type beside the coarser family.
            "rowversion" or "timestamp" => (DatabaseType.VarBinary, null, true),
            "sql_variant" => (null, null, true),

            _ => (null, null, true),
        };
}
