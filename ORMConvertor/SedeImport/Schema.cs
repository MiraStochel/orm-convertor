using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SedeImport;

/// <summary>
/// One column of a SEDE table: named as tables_so.json names it, typed by the listing of the
/// SEDE catalog where the import has it and by the column's category of tables_so.json
/// otherwise (decision 208).
/// </summary>
public sealed record Column(string Name, string Category, string SqlType, bool Nullable, bool IsKey)
{
    /// <summary>The C# type of the Dapper property; nullable where the column is.</summary>
    public string CSharpType => BaseType(SqlType) switch
    {
        "TINYINT" => "byte",
        "SMALLINT" => "short",
        "INT" => "int",
        "BIGINT" => "long",
        "BIT" => "bool",
        "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATE" => "DateTime",
        "UNIQUEIDENTIFIER" => "Guid",
        "NVARCHAR" or "VARCHAR" or "NCHAR" or "CHAR" or "NTEXT" or "TEXT" => "string",
        "FLOAT" or "REAL" => "double",
        "DECIMAL" or "NUMERIC" or "MONEY" => "decimal",
        var other => throw new NotSupportedException($"{Name}: the SQL type {other} has no C# type."),
    } + (Nullable ? "?" : string.Empty);

    public bool IsText => BaseType(SqlType) is "NVARCHAR" or "VARCHAR" or "NCHAR" or "CHAR" or "NTEXT" or "TEXT";

    /// <summary>
    /// How the staging text of the loader becomes the typed value. The dump writes a date as
    /// yyyy-mm-ddThh:mm:ss.fff (ISO 8601, style 126), a flag as True or False, a GUID in its
    /// usual spelling; an empty field is NULL. A text stays as it is - the converter already
    /// wrote an absent attribute as NULL.
    /// </summary>
    public string Conversion(string staged) => BaseType(SqlType) switch
    {
        "BIT" => $"CASE NULLIF({staged}, '') WHEN 'True' THEN 1 WHEN 'False' THEN 0 WHEN '1' THEN 1 WHEN '0' THEN 0 END",
        "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATE" => $"CONVERT({SqlType}, NULLIF({staged}, ''), 126)",
        _ when IsText => staged,
        _ => $"CAST(NULLIF({staged}, '') AS {SqlType})",
    };

    public static string BaseType(string sqlType) => sqlType.Split('(')[0].Trim().ToUpperInvariant();
}

public sealed record Table(string Name, IReadOnlyList<Column> Columns, IReadOnlyList<string> Key)
{
    /// <summary>The file of the data dump the table is loaded from, or null for a table the dump does not carry.</summary>
    public string? DumpFile => Schema.DumpedTables.Contains(Name) ? Name + ".xml" : null;
}

public sealed record ForeignKey(string Table, string Column, string ReferencedTable);

/// <summary>The rows of a type table as the documentation lists them (1 = Question, ...).</summary>
public sealed record Enumeration(string Table, IReadOnlyList<(int Id, string Name)> Values);

/// <summary>
/// The SEDE schema as the import reads it (decision 208): the 29 tables with their columns from
/// tables_so.json of the SEDE repository, the types from the listing of the SEDE catalog
/// (Schema/types.csv, the result of the query the documentation links as "List all fields in
/// all tables on SEDE") where the file is present and from the categories of tables_so.json
/// otherwise, the foreign keys from the links of the schema documentation
/// (Schema/documentation.html, answer 2678 on meta.stackexchange.com) - Stack Exchange declares
/// them nowhere else, and the catalog reads relations from declarations (decision 015) -, and
/// the enumerations of the type tables from the same document.
/// </summary>
public sealed class Schema
{
    /// <summary>The tables the data dump of a site carries, one XML file each; PostTags is derived from Posts.Tags, the type tables are filled from the documentation, the rest stay empty.</summary>
    public static readonly IReadOnlySet<string> DumpedTables = new HashSet<string>(["Badges", "Comments", "PostHistory", "PostLinks", "Posts", "Tags", "Users", "Votes"], StringComparer.Ordinal);

    /// <summary>The type table a documented column enumerates, by the table and the column the enumeration stands under.</summary>
    private static readonly IReadOnlyDictionary<(string Table, string Column), string> EnumeratedTables = new Dictionary<(string, string), string>
    {
        [("Posts", "PostTypeId")] = "PostTypes",
        [("Votes", "VoteTypeId")] = "VoteTypes",
        [("PostHistory", "PostHistoryTypeId")] = "PostHistoryTypes",
        [("PostHistory", "Comment")] = "CloseReasonTypes",
    };

    /// <summary>Columns of the text category that hold a body or a comment, and so a text of any length.</summary>
    private static readonly Regex LongText = new(@"^(Body|Text|AboutMe|Comment|Description|Markdown\w*|InputTitle)$", RegexOptions.Compiled);

    private static readonly Regex ForeignKeyLink = new(@"table=([a-z]+)""[^>]*><code>([A-Za-z]+)</code></a>", RegexOptions.Compiled);
    private static readonly Regex EnumeratedColumn = new(@"<code>(\w+)</code>(?:(?!</li>).)*?<sub>(.*?)</sub>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex EnumerationLine = new(@"^\s*(-?\d+)\s*=\s*(.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    public IReadOnlyList<Table> Tables { get; }

    public IReadOnlyList<ForeignKey> ForeignKeys { get; }

    public IReadOnlyList<Enumeration> Enumerations { get; }

    public IReadOnlyDictionary<string, Table> ByName { get; }

    /// <summary>Whether the types came from the listing of the SEDE catalog, or provisionally from the categories of tables_so.json.</summary>
    public bool TypesFromListing { get; }

    private Schema(IReadOnlyList<Table> tables, IReadOnlyList<ForeignKey> foreignKeys, IReadOnlyList<Enumeration> enumerations, bool typesFromListing)
    {
        Tables = tables;
        ForeignKeys = foreignKeys;
        Enumerations = enumerations;
        TypesFromListing = typesFromListing;
        ByName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);
    }

    public static Schema Read(string checkout, string schemaDirectory, TextWriter log)
    {
        var listing = ReadListing(Path.Combine(schemaDirectory, "types.csv"), log);
        var tables = ReadTables(Path.Combine(checkout, "stackexchange_schema", "tables_so.json"), listing, log);
        var byName = tables.ToDictionary(table => table.Name, StringComparer.OrdinalIgnoreCase);

        var documentation = File.ReadAllText(Path.Combine(schemaDirectory, "documentation.html"));
        var foreignKeys = new List<ForeignKey>();
        var enumerations = new List<Enumeration>();

        foreach (var section in documentation.Split("<h2>").Skip(1))
        {
            var title = Tag.Replace(section[..section.IndexOf("</h2>", StringComparison.Ordinal)], string.Empty).Trim();
            if (title == "xxxTypes")
            {
                continue;
            }

            var owners = title.Split('/').Select(name => name.Trim()).Where(byName.ContainsKey).Select(name => byName[name]).ToList();

            foreach (Match link in ForeignKeyLink.Matches(section))
            {
                if (!byName.TryGetValue(link.Groups[1].Value, out var referenced))
                {
                    continue;
                }

                foreach (var owner in owners)
                {
                    var column = link.Groups[2].Value;
                    if (!owner.Columns.Any(candidate => candidate.Name.Equals(column, StringComparison.Ordinal)))
                    {
                        log.WriteLine($"documentation: {owner.Name}.{column} -> {referenced.Name} skipped, the table has no such column.");
                        continue;
                    }

                    if (!foreignKeys.Any(key => key.Table == owner.Name && key.Column == column))
                    {
                        foreignKeys.Add(new ForeignKey(owner.Name, column, referenced.Name));
                    }
                }
            }

            foreach (Match enumerated in EnumeratedColumn.Matches(section))
            {
                var owner = owners.FirstOrDefault(candidate => EnumeratedTables.ContainsKey((candidate.Name, enumerated.Groups[1].Value)));
                if (owner is null)
                {
                    continue;
                }

                var typeTable = EnumeratedTables[(owner.Name, enumerated.Groups[1].Value)];
                if (enumerations.Any(enumeration => enumeration.Table == typeTable))
                {
                    continue;
                }

                var values = new List<(int, string)>();
                foreach (var line in enumerated.Groups[2].Value.Split("<br />"))
                {
                    var text = WebUtility.HtmlDecode(Tag.Replace(line, string.Empty));
                    if (EnumerationLine.Match(text) is { Success: true } match)
                    {
                        values.Add((int.Parse(match.Groups[1].Value), CleanName(match.Groups[2].Value)));
                    }
                }

                if (values.Count == 0)
                {
                    throw new InvalidDataException($"documentation: {owner.Name}.{enumerated.Groups[1].Value} lists no values for {typeTable}.");
                }

                enumerations.Add(new Enumeration(typeTable, values));
            }
        }

        foreach (var typeTable in EnumeratedTables.Values.Distinct())
        {
            if (!enumerations.Any(enumeration => enumeration.Table == typeTable))
            {
                throw new InvalidDataException($"documentation: no enumeration found for {typeTable}.");
            }
        }

        // Everything downstream - the scripts, the entity files, the tables of a query - goes in the
        // order of the names, so that the catalog's tables of a query are its entity files in order.
        return new Schema([.. tables.OrderBy(table => table.Name, StringComparer.Ordinal)], foreignKeys, enumerations, listing is not null);
    }

    /// <summary>The name of a type as SEDE names it: the words before the dash or the parenthesis that explains it, without quotes.</summary>
    private static string CleanName(string text)
    {
        var name = text;
        foreach (var separator in new[] { " - ", " (", " <" })
        {
            var at = name.IndexOf(separator, StringComparison.Ordinal);
            if (at > 0)
            {
                name = name[..at];
            }
        }

        return name.Trim().Trim('"').Trim();
    }

    private static List<Table> ReadTables(string path, Dictionary<(string Table, string Column), (string Type, bool? Nullable)>? listing, TextWriter log)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement[0] : document.RootElement;

        var names = root.GetProperty("table_names_original").EnumerateArray().Select(name => name.GetString()!).ToList();
        var columns = root.GetProperty("column_names_original").EnumerateArray().Select(pair => (Table: pair[0].GetInt32(), Name: pair[1].GetString()!)).ToList();
        var categories = root.GetProperty("column_types").EnumerateArray().Select(category => category.GetString()!).ToList();

        var tables = new List<Table>();
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            var key = name == "PostTags" ? new[] { "PostId", "TagId" } : new[] { "Id" };
            var own = new List<Column>();

            for (var position = 0; position < columns.Count; position++)
            {
                if (columns[position].Table != index)
                {
                    continue;
                }

                var column = columns[position].Name;
                var category = categories[position];
                var isKey = key.Contains(column, StringComparer.Ordinal);

                string sqlType;
                bool nullable;
                if (listing is not null && listing.TryGetValue((name, column), out var listed))
                {
                    sqlType = listed.Type;
                    nullable = !isKey && (listed.Nullable ?? true);
                }
                else
                {
                    if (listing is not null)
                    {
                        log.WriteLine($"types.csv: {name}.{column} is not listed; its type is provisional.");
                    }

                    sqlType = ProvisionalType(column, category);
                    nullable = !isKey;
                }

                own.Add(new Column(column, category, sqlType, nullable, isKey));
            }

            foreach (var part in key)
            {
                if (!own.Any(column => column.Name == part))
                {
                    throw new InvalidDataException($"tables_so.json: {name} has no key column {part}.");
                }
            }

            tables.Add(new Table(name, own, key));
        }

        return tables;
    }

    /// <summary>
    /// The T-SQL type of a column from its category in tables_so.json, used until the listing of
    /// the SEDE catalog is beside the import: a number is INT, a time DATETIME, a flag BIT, the one
    /// "other" column (a revision GUID) UNIQUEIDENTIFIER, and a text NVARCHAR(250) - a body, a
    /// comment or a description NVARCHAR(MAX).
    /// </summary>
    private static string ProvisionalType(string column, string category) => category switch
    {
        "number" => "INT",
        "time" => "DATETIME",
        "boolean" => "BIT",
        "other" => "UNIQUEIDENTIFIER",
        "text" => LongText.IsMatch(column) ? "NVARCHAR(MAX)" : "NVARCHAR(250)",
        var other => throw new NotSupportedException($"{column}: the category {other} of tables_so.json has no type."),
    };

    /// <summary>
    /// The listing of the SEDE catalog as a CSV with a header: a column naming the table, one
    /// naming the column, one with the type as SQL Server spells it with its length, and
    /// optionally one saying whether the column is nullable. Null where the file is absent.
    /// </summary>
    private static Dictionary<(string Table, string Column), (string Type, bool? Nullable)>? ReadListing(string path, TextWriter log)
    {
        if (!File.Exists(path))
        {
            log.WriteLine("Schema/types.csv is absent: the types are provisional, from the categories of tables_so.json.");
            return null;
        }

        var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var header = Fields(lines[0]).Select(field => field.Trim().ToLowerInvariant()).ToList();

        int Index(Predicate<string> matches, string what)
        {
            var index = header.FindIndex(matches);
            return index >= 0 ? index : throw new InvalidDataException($"types.csv: no column of the header names the {what}: {string.Join(", ", header)}.");
        }

        var table = Index(field => field.Contains("table"), "table");
        var column = Index(field => field.Contains("column"), "column");
        var type = Index(field => field == "type" || field.Contains("data_type") || field.EndsWith("type", StringComparison.Ordinal), "type");
        var nullable = header.FindIndex(field => field.Contains("nullable"));

        var listing = new Dictionary<(string, string), (string, bool?)>();
        foreach (var line in lines.Skip(1))
        {
            var fields = Fields(line);
            // The listing links a table to its sample data as "query://...?table=Posts|Posts";
            // the name is what follows the bar.
            var name = fields[table].Trim();
            name = name[(name.LastIndexOf('|') + 1)..];
            listing[(name, fields[column].Trim())] = (
                ListedType(fields[type].Trim()),
                nullable < 0 ? null : fields[nullable].Trim().Equals("YES", StringComparison.OrdinalIgnoreCase) || fields[nullable].Trim() == "1");
        }

        log.WriteLine($"Schema/types.csv: {listing.Count} columns listed.");
        return listing;
    }

    /// <summary>
    /// The type as T-SQL declares it: the listing writes the precision of a whole number in
    /// parentheses (int(10), tinyint(3)), which T-SQL has no place for, and the unbounded
    /// length of a text as -1 (nvarchar(-1)), which T-SQL spells MAX.
    /// </summary>
    private static string ListedType(string listed)
    {
        var type = listed.ToUpperInvariant();
        var parenthesis = type.IndexOf('(');
        var baseType = parenthesis < 0 ? type : type[..parenthesis];
        return baseType switch
        {
            "INT" or "TINYINT" or "SMALLINT" or "BIGINT" or "BIT" or "DATETIME" or "DATETIME2" or "DATE" or "UNIQUEIDENTIFIER" or "FLOAT" or "REAL" => baseType,
            _ when type.EndsWith("(-1)", StringComparison.Ordinal) => baseType + "(MAX)",
            _ => type,
        };
    }

    private static List<string> Fields(string line)
    {
        var fields = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (character == ',' && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(character);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
