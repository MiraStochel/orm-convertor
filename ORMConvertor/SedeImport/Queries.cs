using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using Tests.Verification;

namespace SedeImport;

/// <summary>One line of val.jsonl or test.jsonl of the SEDE repository, as it publishes it.</summary>
public sealed record SedeEntry(
    [property: JsonPropertyName("QuerySetId")] int QuerySetId,
    [property: JsonPropertyName("Title")] string? Title,
    [property: JsonPropertyName("Description")] string? Description,
    [property: JsonPropertyName("QueryBody")] string QueryBody,
    [property: JsonPropertyName("CreationDate")] string? CreationDate)
{
    /// <summary>The part of the set the line is from: the validation or the test part.</summary>
    public string Set { get; init; } = string.Empty;
}

/// <summary>One parameter of a catalog query: its name without the @, the SQL Server type of its value, and the value the run binds.</summary>
public sealed record CatalogParameter(string Name, string SqlType, string Value);

/// <summary>One query of the catalog, as SampleData/Sede/queries.json carries it.</summary>
public sealed record CatalogQuery(
    string Key,
    int QuerySetId,
    string Set,
    string Title,
    string Description,
    IReadOnlyList<CatalogParameter> Parameters,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    string Sql,
    string SedeSql,
    string Translation,
    string Note);

public sealed record Catalog(string GeneratedBy, IReadOnlyList<CatalogQuery> Queries);

/// <summary>
/// The catalog of the human-reviewed queries of SEDE (decision 208): every query of the
/// validation and the test part, with its title and description as its author wrote them, its
/// text as the author wrote it with the placeholders of SEDE turned into parameters, the value
/// of every parameter, the tables it reads, the columns of its result, the key of its ordering,
/// and its status. The text is the author's: nothing but the placeholders changes, and a text
/// that is not one valid read over the schema stands as not translated with the reason.
/// </summary>
public static class Queries
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>A placeholder of SEDE: ##Name##, ##Name:type##, ##Name?default## or ##Name:type?default##.</summary>
    private static readonly Regex Placeholder = new(@"##([A-Za-z_][A-Za-z0-9_]*)(?::([A-Za-z]+))?(?:\?([^#]*))?##", RegexOptions.Compiled);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private const int ProbeTimeoutSeconds = 30;

    public static IReadOnlyList<SedeEntry> Read(string dataDirectory)
    {
        var entries = new List<SedeEntry>();
        foreach (var (file, set) in new[] { ("val.jsonl", "Validation"), ("test.jsonl", "Test") })
        {
            foreach (var line in File.ReadLines(Path.Combine(dataDirectory, file)).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var entry = JsonSerializer.Deserialize<SedeEntry>(line, ReadOptions) ?? throw new InvalidDataException($"{file}: an empty line.");
                entries.Add(entry with { Set = set });
            }
        }

        return entries;
    }

    /// <summary>
    /// The catalog from the entries. With a connection string, every text is asked of the
    /// database what it returns (sp_describe_first_result_set), which gives the columns of the
    /// result and refuses a text that is not valid over the schema; with the probe, every
    /// translated text is also run once with its values, which finds a text that fails over the
    /// data and notes one that returns no rows.
    /// </summary>
    public static Catalog Convert(IReadOnlyList<SedeEntry> entries, Schema schema, string? connectionString, bool probe, string origin, TextWriter log)
    {
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        using var connection = connectionString is null ? null : new SqlConnection(connectionString);
        connection?.Open();

        var dataSet = connection is null ? null : Scalar<string>(connection, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'sede.dataset'");
        if (probe && dataSet is null)
        {
            throw new InvalidOperationException("The probe needs a database holding the data (the extended property sede.dataset), and the given one holds none.");
        }

        var queries = new List<CatalogQuery>();
        var seen = new HashSet<int>();
        var fallbacks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int notRead = 0, invalid = 0, failing = 0, empty = 0, withParameters = 0;

        foreach (var entry in entries.OrderBy(entry => entry.Set == "Validation" ? 0 : 1).ThenBy(entry => entry.QuerySetId))
        {
            if (!seen.Add(entry.QuerySetId))
            {
                throw new InvalidDataException($"QuerySetId {entry.QuerySetId} occurs twice.");
            }

            var key = $"s{entry.QuerySetId}";
            var sedeSql = entry.QueryBody.Replace("\r", string.Empty).Trim();
            var sql = Placeholders.Convert(sedeSql, out var placeholders, key, log);
            var parameters = placeholders.Select(placeholder => Parameter(placeholder, fallbacks)).ToList();
            if (parameters.Count > 0)
            {
                withParameters++;
            }

            string? translation = null;
            string note = string.Empty;
            var tables = new List<string>();
            var columns = new List<string>();
            var orderingKey = new List<string>();

            var fragment = parser.Parse(new StringReader(sql), out var errors);
            if (errors.Count > 0)
            {
                translation = "NotTranslated";
                note = $"The text does not parse: {errors[0].Message} (line {errors[0].Line}).";
            }
            else
            {
                tables = TablesOf(fragment, schema);
                if (Shape(fragment) is { } shape)
                {
                    translation = "NotTranslated";
                    note = shape;
                }
            }

            if (connection is not null && errors.Count == 0)
            {
                var described = Describe(connection, sql, parameters, out var describeError);
                if (described is null)
                {
                    if (translation is null)
                    {
                        translation = "NotTranslated";
                        note = $"The text is not valid over the schema: {describeError}";
                    }
                }
                else
                {
                    columns = described;
                    if (translation is null && columns.Any(string.IsNullOrEmpty))
                    {
                        translation = "NotTranslated";
                        note = "A column of the result has no name, so no typed target can carry the row.";
                    }
                    else if (translation is null && columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Count)
                    {
                        var shared = columns.GroupBy(column => column, StringComparer.OrdinalIgnoreCase).First(group => group.Count() > 1).Key;
                        translation = "NotTranslated";
                        note = $"Two columns of the result share the name {shared}, so no typed target can carry the row.";
                    }
                    else if (translation is null && tables.Count == 0)
                    {
                        translation = "NotTranslated";
                        note = "The text reads no table of the schema - the catalog views of the server, or no table at all.";
                    }
                    else if (translation is null)
                    {
                        orderingKey = KeyFromTheTool(schema, tables, sql, columns, key, log) ?? OrderingKeyOf(fragment, columns);
                    }
                }
            }

            if (translation is null && probe && connection is not null)
            {
                var outcome = Probe(connection, sql, parameters, out var probeError);
                if (outcome == ProbeOutcome.Failed)
                {
                    translation = "NotTranslated";
                    note = $"The text fails over the data with the stated values: {probeError}";
                    failing++;
                }
                else if (outcome == ProbeOutcome.Empty)
                {
                    note = $"Returns no rows over {dataSet} with the stated values.";
                    empty++;
                }
                else if (outcome == ProbeOutcome.TimedOut)
                {
                    log.WriteLine($"{key}: the probe did not finish in {ProbeTimeoutSeconds} s; the run has its own limit.");
                }
            }

            if (translation == "NotTranslated")
            {
                if (note.StartsWith("The text is not one read", StringComparison.Ordinal) || note.StartsWith("The text does not parse", StringComparison.Ordinal))
                {
                    notRead++;
                }
                else
                {
                    invalid++;
                }
            }

            queries.Add(new CatalogQuery(
                key,
                entry.QuerySetId,
                entry.Set,
                Whitespace.Replace(entry.Title ?? string.Empty, " ").Trim(),
                Whitespace.Replace(entry.Description ?? string.Empty, " ").Trim(),
                parameters,
                tables,
                columns,
                orderingKey,
                sql,
                sedeSql,
                translation ?? "AsSpecified",
                note));
        }

        log.WriteLine($"{queries.Count} queries; {withParameters} with parameters; {notRead} not one read statement or not parsed, {invalid} not valid over the schema, {failing} failing over the data; {empty} return no rows with their values.");
        if (fallbacks.Count > 0)
        {
            log.WriteLine($"Parameters whose value came from the type alone (Values.cs has no entry): {string.Join(", ", fallbacks)}.");
        }

        var checkedAgainst = connection is null
            ? "no database (the statuses are syntactic only)"
            : $"{connection.Database} holding {dataSet ?? "no data set"}{(probe ? ", every translated text run once" : string.Empty)}";

        return new Catalog(
            $"SedeImport over {origin}, types {(schema.TypesFromListing ? "from the listing of the SEDE catalog" : "provisional from tables_so.json")}, statuses checked against {checkedAgainst}. Generated: change the tool and run it again rather than this file.",
            queries);
    }

    public static string Write(Catalog catalog) => JsonSerializer.Serialize(catalog, WriteOptions) + Environment.NewLine;

    /// <summary>
    /// The type and the value of one parameter. A declared type is the author's (int, string,
    /// float); an undeclared one is read off the value: a whole number is INT, a decimal number
    /// FLOAT, anything else NVARCHAR(MAX). The value is the author's default, or the one chosen
    /// over the data for the name (Values).
    /// </summary>
    private static CatalogParameter Parameter(Placeholders.Found placeholder, SortedSet<string> fallbacks)
    {
        var declared = placeholder.Type?.ToLowerInvariant() switch
        {
            null => null,
            "int" => "INT",
            "string" => "NVARCHAR(MAX)",
            "float" => "FLOAT",
            var other => throw new InvalidDataException($"The placeholder ##{placeholder.Name}:{other}## has a type the import does not know."),
        };

        string value;
        if (placeholder.Default is { } given && !string.IsNullOrWhiteSpace(given))
        {
            value = given.Trim();
        }
        else
        {
            var (chosen, fromTable) = Values.For(placeholder.Name, declared ?? (LooksNumeric(placeholder.Name) ? "INT" : "NVARCHAR(MAX)"));
            value = chosen;
            if (!fromTable)
            {
                fallbacks.Add(placeholder.Name);
            }
        }

        var sqlType = declared ?? (Values.IsInteger(value) ? (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? "INT" : "BIGINT") : Values.IsDecimal(value) ? "FLOAT" : "NVARCHAR(MAX)");
        if (sqlType == "INT" && !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            sqlType = Values.IsInteger(value) ? "BIGINT" : "INT";
        }

        return new CatalogParameter(placeholder.Name, sqlType, value);
    }

    private static readonly Regex NumericName = new(@"(?i)(id|num|count|limit|top|^n$|min|max|days|year|month|score|rep|threshold|percent)", RegexOptions.Compiled);

    private static bool LooksNumeric(string name) => NumericName.IsMatch(name);

    /// <summary>Why the text is not one read statement, or null when it is: one batch, one SELECT without INTO.</summary>
    private static string? Shape(TSqlFragment fragment)
    {
        if (fragment is not TSqlScript script)
        {
            return "The text is not one read statement.";
        }

        var statements = script.Batches.SelectMany(batch => batch.Statements).ToList();
        if (statements.Count != 1)
        {
            var kinds = statements.Select(statement => statement.GetType().Name.Replace("Statement", string.Empty)).Distinct();
            return $"The text is not one read statement but {statements.Count}: {string.Join(", ", kinds)}.";
        }

        if (statements[0] is not SelectStatement select)
        {
            return $"The text is not one read statement but a {statements[0].GetType().Name.Replace("Statement", string.Empty)}.";
        }

        if (select.Into is not null)
        {
            return "The text is not one read statement: its SELECT writes INTO a table.";
        }

        return null;
    }

    /// <summary>The tables of the schema the text names, each once, in the order of the schema.</summary>
    private static List<string> TablesOf(TSqlFragment fragment, Schema schema)
    {
        var visitor = new TableVisitor();
        fragment.Accept(visitor);
        return schema.Tables.Where(table => visitor.Names.Contains(table.Name)).Select(table => table.Name).ToList();
    }

    private sealed class TableVisitor : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(NamedTableReference node)
        {
            if (node.SchemaObject.BaseIdentifier is { } identifier)
            {
                Names.Add(identifier.Value);
            }
        }
    }

    /// <summary>
    /// The columns of the result as the database describes them, or null with the error where the
    /// text is not valid over the schema. The parameters are declared with their types, as the
    /// run binds them.
    /// </summary>
    private static List<string>? Describe(SqlConnection connection, string sql, IReadOnlyList<CatalogParameter> parameters, out string error)
    {
        error = string.Empty;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "EXEC sp_describe_first_result_set @tsql = @tsql, @params = @params, @browse_information_mode = 0;";
            command.CommandTimeout = ProbeTimeoutSeconds;
            command.Parameters.Add(new SqlParameter("@tsql", SqlDbType.NVarChar, -1) { Value = sql });
            command.Parameters.Add(new SqlParameter("@params", SqlDbType.NVarChar, -1) { Value = string.Join(", ", parameters.Select(parameter => $"@{parameter.Name} {parameter.SqlType}")) });

            var columns = new List<(int Ordinal, string Name)>();
            using var reader = command.ExecuteReader();
            var ordinalColumn = reader.GetOrdinal("column_ordinal");
            var nameColumn = reader.GetOrdinal("name");
            var hiddenColumn = reader.GetOrdinal("is_hidden");
            while (reader.Read())
            {
                if (!reader.IsDBNull(hiddenColumn) && reader.GetBoolean(hiddenColumn))
                {
                    continue;
                }

                columns.Add((reader.GetInt32(ordinalColumn), reader.IsDBNull(nameColumn) ? string.Empty : reader.GetString(nameColumn)));
            }

            return columns.OrderBy(column => column.Ordinal).Select(column => column.Name).ToList();
        }
        catch (SqlException exception)
        {
            error = Whitespace.Replace(exception.Message, " ").Trim();
            return null;
        }
    }

    private enum ProbeOutcome { Rows, Empty, Failed, TimedOut }

    /// <summary>One execution of the text with its values: whether it returned a row, none, failed, or did not finish.</summary>
    private static ProbeOutcome Probe(SqlConnection connection, string sql, IReadOnlyList<CatalogParameter> parameters, out string error)
    {
        error = string.Empty;
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = ProbeTimeoutSeconds;
            foreach (var parameter in parameters)
            {
                command.Parameters.Add(SqlParameterOf(parameter));
            }

            using var reader = command.ExecuteReader();
            return reader.Read() ? ProbeOutcome.Rows : ProbeOutcome.Empty;
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            return ProbeOutcome.TimedOut;
        }
        catch (SqlException exception)
        {
            error = Whitespace.Replace(exception.Message, " ").Trim();
            return ProbeOutcome.Failed;
        }
    }

    /// <summary>The parameter typed and valued as the run binds it: the value of the catalog converted by its SQL type.</summary>
    public static SqlParameter SqlParameterOf(CatalogParameter parameter) => Column.BaseType(parameter.SqlType) switch
    {
        "INT" => new SqlParameter("@" + parameter.Name, SqlDbType.Int) { Value = int.Parse(parameter.Value, CultureInfo.InvariantCulture) },
        "BIGINT" => new SqlParameter("@" + parameter.Name, SqlDbType.BigInt) { Value = long.Parse(parameter.Value, CultureInfo.InvariantCulture) },
        "FLOAT" => new SqlParameter("@" + parameter.Name, SqlDbType.Float) { Value = double.Parse(parameter.Value, CultureInfo.InvariantCulture) },
        _ => new SqlParameter("@" + parameter.Name, SqlDbType.NVarChar, -1) { Value = parameter.Value },
    };

    /// <summary>
    /// The ordering key of the query as the run compares rows by it (decision 170): what the tool
    /// reads off the intermediate representation of the text, through the very helper the suite
    /// holds the FIBEN catalog with - because the source of the comparison is the tool's own
    /// Dapper artifact, and an ordering the tool reads as a loss is no ordering of the source.
    /// The text is read with the entities of the tables it names and no catalog. Null where the
    /// tool refuses the text, whose key the run then never uses.
    /// </summary>
    private static List<string>? KeyFromTheTool(Schema schema, IReadOnlyList<string> tables, string sql, IReadOnlyList<string> columns, string key, TextWriter log)
    {
        var units = tables.Select(table => new ConversionSource
        {
            Name = table + ".cs",
            ContentType = ConversionContentType.CSharp,
            Content = Scripts.Entity(schema.ByName[table]),
        }).ToList();
        units.Add(new ConversionSource { Name = key + ".sql", ContentType = ConversionContentType.SqlQuery, Content = sql });

        try
        {
            return [.. OrderingKeys.Of(SourceQueries.ReadSingle(ORMEnum.Dapper, units), columns)];
        }
        catch (Exception exception)
        {
            log.WriteLine($"{key}: the tool does not read the text ({Whitespace.Replace(exception.Message, " ").Trim()}); the ordering key is read from the text.");
            return null;
        }
    }

    /// <summary>
    /// The ordering key of the outermost query over the columns of its result, read from the text
    /// alone for a query the tool refuses, after the rule of decision 170: for every key of its
    /// ordering, in order, the column the key falls on - a column reference by its name, a number
    /// by its position, any other expression by a projected expression that reads alike -, each
    /// once; every column where some key falls on none; nothing where the text does not order; and
    /// nothing for a set operation, whose ordering the representation does not carry.
    /// </summary>
    private static List<string> OrderingKeyOf(TSqlFragment fragment, IReadOnlyList<string> columns)
    {
        var select = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
        var expression = select.QueryExpression;
        while (expression is QueryParenthesisExpression parenthesis)
        {
            expression = parenthesis.QueryExpression;
        }

        if (expression is not QuerySpecification specification || specification.OrderByClause is null)
        {
            return [];
        }

        var projected = new List<(string? Name, string? Text)>();
        foreach (var element in specification.SelectElements)
        {
            switch (element)
            {
                case SelectScalarExpression scalar:
                    projected.Add((scalar.ColumnName?.Value ?? (scalar.Expression is ColumnReferenceExpression reference ? reference.MultiPartIdentifier.Identifiers[^1].Value : null), Text(fragment, scalar.Expression)));
                    break;
                case SelectStarExpression:
                    projected.Add((null, null));
                    break;
            }
        }

        var key = new List<string>();
        foreach (var element in specification.OrderByClause.OrderByElements)
        {
            string? column = element.Expression switch
            {
                ColumnReferenceExpression reference => Match(columns, reference.MultiPartIdentifier.Identifiers[^1].Value)
                    ?? projected.FirstOrDefault(item => item.Text is not null && item.Text == Text(fragment, reference)).Name,
                IntegerLiteral literal when int.TryParse(literal.Value, out var ordinal) && ordinal >= 1 && ordinal <= columns.Count => columns[ordinal - 1],
                var other => projected.FirstOrDefault(item => item.Text is not null && item.Text == Text(fragment, other)).Name is { } name ? Match(columns, name) : null,
            };

            if (column is null)
            {
                return [.. columns];
            }

            if (!key.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                key.Add(column);
            }
        }

        return key;
    }

    private static string? Match(IReadOnlyList<string> columns, string name)
        => columns.FirstOrDefault(column => column.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The tokens of a fragment without whitespace and comments, upper-cased: two expressions read alike when these are equal.</summary>
    private static string Text(TSqlFragment root, TSqlFragment fragment)
    {
        var text = new StringBuilder();
        for (var index = fragment.FirstTokenIndex; index <= fragment.LastTokenIndex; index++)
        {
            var token = root.ScriptTokenStream[index];
            if (token.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.MultilineComment or TSqlTokenType.SingleLineComment)
            {
                continue;
            }

            text.Append(token.Text.Trim('[', ']').ToUpperInvariant());
        }

        return text.ToString();
    }

    private static T? Scalar<T>(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default : (T)value;
    }
}

/// <summary>
/// The one change the import makes to an author's text (decision 208): the placeholders of SEDE
/// become parameters. ##Name##, ##Name:type## and ##Name:type?default## become @Name; inside a
/// string literal, where SEDE pastes the value into the text, the literal becomes a concatenation
/// of its parts and the parameter. One name in two spellings is one parameter in the first
/// spelling; a name the text also declares as its own variable gets the prefix p_, so that the
/// text stays valid; and a placeholder that is the row count of a TOP goes in parentheses, which
/// T-SQL wants of a variable there and SEDE did not need of a pasted number.
/// </summary>
public static class Placeholders
{
    public sealed record Found(string Name, string? Type, string? Default);

    private static readonly Regex Placeholder = new(@"##([A-Za-z_][A-Za-z0-9_]*)(?::([A-Za-z]+))?(?:\?([^#]*))?##", RegexOptions.Compiled);

    /// <summary>The text so far ends in TOP and whitespace, so the placeholder is a row count, which T-SQL takes from a variable only in parentheses.</summary>
    private static readonly Regex TopBefore = new(@"(?i)\bTOP\s+$", RegexOptions.Compiled);

    public static string Convert(string sql, out List<Found> parameters, string key, TextWriter log)
    {
        var found = new List<Found>();
        var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string NameOf(Match match)
        {
            var name = match.Groups[1].Value;
            if (!spelling.TryGetValue(name, out var first))
            {
                first = Regex.IsMatch(sql, @"(?i)\b(DECLARE|SET)\s+@" + Regex.Escape(name) + @"\b") ? "p_" + name : name;
                if (first != name)
                {
                    log.WriteLine($"{key}: the text declares @{name} itself; the placeholder became @{first}.");
                }

                spelling[name] = first;
                found.Add(new Found(first, match.Groups[2].Success ? match.Groups[2].Value : null, match.Groups[3].Success ? match.Groups[3].Value : null));
            }

            return first;
        }

        var text = new StringBuilder(sql.Length);
        var index = 0;
        while (index < sql.Length)
        {
            var character = sql[index];

            if (character == '\'')
            {
                var end = index + 1;
                while (end < sql.Length)
                {
                    if (sql[end] == '\'')
                    {
                        if (end + 1 < sql.Length && sql[end + 1] == '\'')
                        {
                            end += 2;
                            continue;
                        }

                        break;
                    }

                    end++;
                }

                var literal = sql[(index + 1)..Math.Min(end, sql.Length)];
                var matches = Placeholder.Matches(literal);
                if (matches.Count == 0)
                {
                    text.Append(sql, index, Math.Min(end + 1, sql.Length) - index);
                }
                else
                {
                    var pieces = new List<string>();
                    var last = 0;
                    foreach (Match match in matches)
                    {
                        if (match.Index > last)
                        {
                            pieces.Add($"'{literal[last..match.Index]}'");
                        }

                        pieces.Add("@" + NameOf(match));
                        last = match.Index + match.Length;
                    }

                    if (last < literal.Length)
                    {
                        pieces.Add($"'{literal[last..]}'");
                    }

                    text.Append(pieces.Count == 1 ? pieces[0] : "(" + string.Join(" + ", pieces) + ")");
                }

                index = Math.Min(end + 1, sql.Length);
                continue;
            }

            if (character == '#' && Placeholder.Match(sql, index) is { Success: true } placeholder && placeholder.Index == index)
            {
                // TOP ##n## is valid for SEDE, which pastes the number in; TOP @n is not valid
                // T-SQL, which wants a variable in a row count in parentheses: TOP (@n).
                var afterTop = TopBefore.IsMatch(text.ToString());
                text.Append(afterTop ? "(@" : "@").Append(NameOf(placeholder)).Append(afterTop ? ")" : string.Empty);
                index += placeholder.Length;
                continue;
            }

            text.Append(character);
            index++;
        }

        parameters = found;
        return text.ToString();
    }
}
