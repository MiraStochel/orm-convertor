using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FibenImport;

/// <summary>One entry of FIBEN_Queries.json as the benchmark publishes it.</summary>
public sealed record BenchmarkEntry(
    [property: JsonPropertyName("uniqueQueryID")] int UniqueQueryId,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("isParaphrased")] bool IsParaphrased,
    [property: JsonPropertyName("SQL")] string Sql,
    [property: JsonPropertyName("queryType")] string QueryType);

/// <summary>One query of the catalog, as SampleData/Fiben/queries.json carries it.</summary>
public sealed record CatalogQuery(
    string Key,
    int Number,
    string Nesting,
    IReadOnlyList<string> Questions,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    string Sql,
    string BenchmarkSql,
    string Translation,
    string Note);

public sealed record Catalog(string GeneratedBy, IReadOnlyList<CatalogQuery> Queries);

/// <summary>
/// The conversion of the 237 distinct SQL queries of the benchmark from the Db2 dialect into
/// T-SQL over the tables of schema.sql (decision 149). The rules are few and mechanical, and a
/// query they do not carry over whole is patched by name with a reason (<see cref="Patches"/>),
/// so that every difference from the benchmark's text is written down here and nowhere else.
/// The text after conversion is the tool's own text, which is why the judge of the catalog is
/// made from a documented run and not taken over from anywhere.
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

    /// <summary>
    /// Replacements applied to the benchmark's text before the rules, each one the smallest
    /// change that makes the query T-SQL or makes it read its question. A patch that no longer
    /// matches stops the import, so none can go stale silently.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, (string From, string To, string Reason)[]> Patches = new Dictionary<int, (string, string, string)[]>
    {
        // The benchmark's text lacks the parenthesis that closes its IN list; no SQL dialect runs it as published.
        [136] = [("HAVING Avg(ElementOfFinancialStatement1.\"HASMETRICVALUE\") > 1.0E6", "HAVING Avg(ElementOfFinancialStatement1.\"HASMETRICVALUE\") > 1.0E6)", "the published text does not close the parenthesis of its IN list.")],

        // The benchmark's text lacks the quote that closes its last literal; no SQL dialect runs it as published.
        [172] = [(" oSecuritiesTransaction.\"HASSETTLEMENTDATE\" <= '2017-12-31 23:59:00.000", " oSecuritiesTransaction.\"HASSETTLEMENTDATE\" <= '2017-12-31 23:59:00.000'", "the published text does not close its last string literal.")],

        // Db2 accepts a GROUP BY over a column of the outer query; SQL Server refuses it (error
        // 164). The inner query already equates that column with its own, so grouping by its own
        // column makes the same single group.
        [218] = [("GROUP BY oPerson.\"HASPERSONNAME\")", "GROUP BY oInnerPerson.\"HASPERSONNAME\")", "SQL Server refuses a GROUP BY over an outer reference; the inner column it equals groups the same.")],
        [219] = [("GROUP BY oPerson.\"HASPERSONNAME\")", "GROUP BY oInnerPerson.\"HASPERSONNAME\")", "SQL Server refuses a GROUP BY over an outer reference; the inner column it equals groups the same.")],
        [220] = [("GROUP BY oPerson.\"HASPERSONNAME\")", "GROUP BY oInnerPerson.\"HASPERSONNAME\")", "SQL Server refuses a GROUP BY over an outer reference; the inner column it equals groups the same.")],
        [227] = [("GROUP BY oPerson.\"HASPERSONNAME\")", "GROUP BY oInnerPerson.\"HASPERSONNAME\")", "SQL Server refuses a GROUP BY over an outer reference; the inner column it equals groups the same.")],

        // The benchmark names three nested derived tables alike, a, each shadowing the one inside
        // it; the tool finds a derived table by its name and refuses the shadowing, so the three
        // are told apart from the inside out. The rows are the same.
        [99] = [
            ("FROM (SELECT a.*, monthnum - dense_rank() over (partition by oPerson_personID order by monthnum) as groupnum FROM (SELECT",
             "FROM (SELECT months.*, monthnum - dense_rank() over (partition by oPerson_personID order by monthnum) as groupnum FROM (SELECT",
             "three nested derived tables are all named a; named apart so that each is found by its name."),
            ("AND '2016-12-31 23:59:00.000') a) a) a WHERE", "AND '2016-12-31 23:59:00.000') months) runs) stretches WHERE",
             "three nested derived tables are all named a; named apart so that each is found by its name."),
            ("SELECT a.*, max(monthnum) over (partition by oPerson_personID, groupnum) as lastmonth",
             "SELECT runs.*, max(monthnum) over (partition by oPerson_personID, groupnum) as lastmonth",
             "three nested derived tables are all named a; named apart so that each is found by its name."),
        ],

        // Db2 lets a derived table go without a name; T-SQL does not.
        [237] = [(") WHERE numOfficers > industryAverage", ") AS perCorporation WHERE numOfficers > industryAverage", "T-SQL requires an alias on a derived table.")],
    };

    /// <summary>
    /// The status of a query the tool does not translate as the benchmark states it, with the
    /// note the page shows; every other query is translated as specified. A status is a claim
    /// the on-demand run checks: a query marked as not translated has to be refused.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, (string Translation, string Note)> Statuses = new Dictionary<int, (string, string)>
    {
        // Every query is translated as specified: the aggregates over a window of q99 and q237
        // entered the vocabulary with decision 161, the subqueries in the ON condition of an
        // inner join of q124-126 and q200-202 with decision 162.
    };

    private static readonly Regex SchemaQualified = new(@"FIBEN\.""(\w+)""", RegexOptions.Compiled);
    private static readonly Regex Quoted = new(@"""(\w+)""", RegexOptions.Compiled);
    private static readonly Regex FunctionName = new(@"\b(sum|count|avg|min|max|year|month|dense_rank)\s*\(", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Keyword = new(
        @"\b(select|from|where|inner join|left outer join|left join|join|on|and|or|not in|in|group by|having|order by|union all|union|as|over|partition by|desc|asc|between|with|max|min)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TableReference = new(@"\b(?:FROM|JOIN)\s+(\w+)(?:\s+(?!ON\b|WHERE\b|INNER\b|LEFT\b|GROUP\b|ORDER\b|UNION\b|HAVING\b)(\w+))?", RegexOptions.Compiled);
    private static readonly Regex QuotedNumber = new(@"\b(\w+)\.(\w+)\s*(=|<>|>=|<=|<|>)\s*'(-?\d+(?:\.\d+)?)'", RegexOptions.Compiled);
    private static readonly Regex FetchFirst = new(@"\s*FETCH FIRST (\d+) ROWS? ONLY", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static IReadOnlyList<BenchmarkEntry> Read(string path)
        => JsonSerializer.Deserialize<List<BenchmarkEntry>>(File.ReadAllText(path), ReadOptions)
            ?? throw new InvalidDataException("FIBEN_Queries.json is empty.");

    public static Catalog Convert(IReadOnlyList<BenchmarkEntry> entries, Ddl ddl, string origin, TextWriter log)
    {
        var queries = new List<CatalogQuery>();
        var unquoted = 0;

        foreach (var group in entries.GroupBy(entry => entry.UniqueQueryId).OrderBy(group => group.Key))
        {
            var primary = group.Single(entry => !entry.IsParaphrased);
            var questions = group.OrderBy(entry => entry.IsParaphrased).Select(entry => Whitespace.Replace(entry.Question, " ").Trim()).Distinct().ToList();

            var sql = primary.Sql.Trim();
            if (Patches.TryGetValue(group.Key, out var patches))
            {
                foreach (var (from, to, reason) in patches)
                {
                    var occurrences = (sql.Length - sql.Replace(from, string.Empty, StringComparison.Ordinal).Length) / from.Length;
                    if (occurrences != 1)
                    {
                        throw new InvalidDataException($"Query {group.Key}: the patch \"{from}\" matches {occurrences} times, not once ({reason}).");
                    }

                    sql = sql.Replace(from, to, StringComparison.Ordinal);
                    log.WriteLine($"q{group.Key}: patched - {reason}");
                }
            }

            var converted = ConvertText(sql, ddl, group.Key, log, ref unquoted, out var columns, out var orderingKey);
            var tables = Tables(converted, ddl);

            queries.Add(new CatalogQuery(
                $"q{group.Key}",
                group.Key,
                Nesting(primary.QueryType),
                questions,
                tables,
                columns,
                orderingKey,
                converted,
                Whitespace.Replace(primary.Sql, " ").Trim(),
                Statuses.TryGetValue(group.Key, out var status) ? status.Translation : "AsSpecified",
                Statuses.TryGetValue(group.Key, out status) ? status.Note : string.Empty));
        }

        log.WriteLine($"{queries.Count} queries converted; {unquoted} quoted numbers compared with a numeric column unquoted.");
        return new Catalog(
            $"FibenImport over {origin}. Generated: change the tool and run it again rather than this file.",
            queries);
    }

    public static string Write(Catalog catalog) => JsonSerializer.Serialize(catalog, WriteOptions) + Environment.NewLine;

    private static string Nesting(string queryType) => queryType.Trim().ToLowerInvariant() switch
    {
        "non-nested" => "NonNested",
        "type-n" => "TypeN",
        "type-a" => "TypeA",
        "type-j" => "TypeJ",
        "type-ja" => "TypeJA",
        var other => throw new InvalidDataException($"The query type \"{other}\" is not one of Kim's."),
    };

    private static string ConvertText(string sql, Ddl ddl, int number, TextWriter log, ref int unquoted, out IReadOnlyList<string> columns, out IReadOnlyList<string> orderingKey)
    {
        // 1. The schema and the quotes: FIBEN."X" is X, "COL" is COL. Three queries of the
        //    benchmark are written without either; they come through the same.
        sql = SchemaQualified.Replace(sql, "$1");
        sql = Quoted.Replace(sql, "$1");
        sql = Whitespace.Replace(sql, " ").Trim();
        sql = sql.Replace("( ", "(", StringComparison.Ordinal).Replace(" )", ")", StringComparison.Ordinal);
        // A literal glued to the keyword after it ('2020'AND) is two tokens for Db2 and SQL Server alike; spelled apart here.
        sql = SeparateLiterals(sql);

        // 2. One spelling of functions and keywords; the benchmark mixes Sum, SUM and sum.
        sql = FunctionName.Replace(sql, match => match.Groups[1].Value.ToUpperInvariant() + "(");
        sql = Keyword.Replace(sql, match => match.Value.ToUpperInvariant());

        // 3. A number in quotes compared with a numeric column ('2015' against a BIGINT year)
        //    is the number: Db2 and SQL Server both convert it, a typed target cannot.
        var aliases = Aliases(sql, ddl);
        var count = 0;
        sql = QuotedNumber.Replace(sql, match =>
        {
            if (aliases.TryGetValue(match.Groups[1].Value, out var table)
                && table.Columns.FirstOrDefault(column => column.Name.Equals(match.Groups[2].Value, StringComparison.OrdinalIgnoreCase)) is { IsNumeric: true })
            {
                count++;
                return $"{match.Groups[1].Value}.{match.Groups[2].Value} {match.Groups[3].Value} {match.Groups[4].Value}";
            }

            return match.Value;
        });
        unquoted += count;

        // 4. FETCH FIRST n ROWS ONLY becomes TOP (n) of the SELECT that owns it - the last SELECT
        //    at the same depth of parentheses before the clause.
        sql = MoveRowLimits(sql, number, log);

        // 5. Every projected column has a name: an aggregate without one is named AggResult<n>,
        //    as the benchmark names its own, because a typed target materializes a row by name.
        //    The names of the outermost projection go into the catalog for the run that
        //    compares rows.
        sql = NameProjection(sql, number, log, out columns);

        // 6. A slice that does not determine the rows it returns gets the deciding key
        //    (decision 170): the keys of its grouping or the expressions of its projection the
        //    ordering lacks, appended ascending, so that every result of the text is a result
        //    the benchmark's text admits and only one of them.
        sql = DecideSlices(sql, number, log);

        // The ordering key of the outermost query over the names of its projection goes into
        // the catalog beside the columns: what the run keeps the order of (decision 170).
        orderingKey = OrderingKeyOf(sql, number, columns);

        return Format(sql);
    }

    private static readonly Regex Direction = new(@"\s+(ASC|DESC)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WholeRow = new(@"^(?:\w+\.)?\*$", RegexOptions.Compiled);
    private static readonly Regex AggregateCall = new(@"^(?:COUNT|SUM|AVG|MIN|MAX)\(", RegexOptions.Compiled);

    /// <summary>One item of a select list: its expression without the alias, and the name the row carries it under, if it has one.</summary>
    private sealed record ProjectedItem(string Expression, string? Name);

    /// <summary>The clauses of one scope that the rule of decision 170 reads: its select list, its grouping, its ordering, and where the scope ends.</summary>
    private sealed record ScopeClauses(List<ProjectedItem> Projection, List<string> GroupBy, List<string> OrderBy, int End);

    /// <summary>
    /// The sixth rule of the conversion (decision 170). A scope sliced by TOP determines its rows
    /// when (a) it is an aggregate without a grouping, (b) its ordering contains every key of its
    /// grouping, or (c) its ordering contains every expression of its projection - read from the
    /// text alone, as <c>FibenCatalogTest</c> reads the same condition from the intermediate
    /// representation. A scope that meets none gets the missing keys of its grouping, or the
    /// missing expressions of its projection, appended to its ordering ascending as the clause
    /// writes them; a scope without an ordering gets one. The keys of the benchmark's text stay
    /// in front, so every result of the refined text is one the benchmark's text admits.
    /// </summary>
    private static string DecideSlices(string sql, int number, TextWriter log)
    {
        var edits = new List<(int Position, string Text)>();

        for (var at = IndexOfSlicedSelect(sql, 0); at >= 0; at = IndexOfSlicedSelect(sql, at + 1))
        {
            var scope = ReadScope(sql, number, at, ScopeEnd(sql, number, at));
            var ordering = scope.OrderBy
                .Select(item => Resolved(Direction.Replace(item, string.Empty), scope.Projection))
                .ToHashSet(StringComparer.Ordinal);

            List<string> missing;
            if (scope.GroupBy.Count == 0 && scope.Projection.All(item => AggregateCall.IsMatch(Normalized(item.Expression))))
            {
                missing = [];
            }
            else if (scope.GroupBy.Count > 0)
            {
                missing = scope.GroupBy.Where(key => !ordering.Contains(Normalized(key))).ToList();
            }
            else
            {
                missing = scope.Projection.Where(item => !ordering.Contains(Normalized(item.Expression))).Select(item => item.Expression).ToList();
            }

            if (missing.Count == 0)
            {
                continue;
            }

            edits.Add((scope.End, (scope.OrderBy.Count > 0 ? ", " : " ORDER BY ") + string.Join(", ", missing)));
            log.WriteLine($"q{number}: a slice that did not determine its rows got the deciding key {string.Join(", ", missing)}.");
        }

        var text = new StringBuilder(sql);
        foreach (var (position, insertion) in edits.OrderByDescending(edit => edit.Position))
        {
            text.Insert(position, insertion);
        }

        return text.ToString();
    }

    /// <summary>
    /// The ordering key of the outermost query over the names of its projection (decision 170):
    /// for every key of its ordering the name of the projected item it falls on - an alias, or an
    /// item of the same expression -, each once; every column where some key falls on no item,
    /// because the segments of equal key are then not visible in the result; nothing without an
    /// ordering.
    /// </summary>
    private static IReadOnlyList<string> OrderingKeyOf(string sql, int number, IReadOnlyList<string> columns)
    {
        var scope = ReadScope(sql, number, MainSelect(sql), sql.Length);

        if (!scope.Projection.Select(item => item.Name).SequenceEqual(columns, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Query {number}: the projection reads as {string.Join(", ", scope.Projection.Select(item => item.Name))} and was named {string.Join(", ", columns)}.");
        }

        var key = new List<string>();
        foreach (var item in scope.OrderBy)
        {
            var expression = Direction.Replace(item, string.Empty);
            var named = scope.Projection.FirstOrDefault(candidate => candidate.Name is not null && candidate.Name.Equals(expression, StringComparison.OrdinalIgnoreCase))
                ?? scope.Projection.FirstOrDefault(candidate => Normalized(candidate.Expression) == Normalized(expression));

            if (named?.Name is null)
            {
                return columns;
            }

            if (!key.Contains(named.Name, StringComparer.OrdinalIgnoreCase))
            {
                key.Add(named.Name);
            }
        }

        return key;
    }

    /// <summary>The SELECT that starts the main query: after the definitions of a WITH, the first SELECT at depth 0.</summary>
    private static int MainSelect(string sql)
    {
        if (!sql.StartsWith("WITH ", StringComparison.Ordinal))
        {
            return 0;
        }

        var depth = 0;
        for (var index = 0; index < sql.Length; index++)
        {
            if (sql[index] == '(') depth++;
            else if (sql[index] == ')') depth--;
            else if (depth == 0 && IsWord(sql, index, "SELECT")) return index;
        }

        throw new InvalidDataException("A WITH without a main query.");
    }

    private static int IndexOfSlicedSelect(string sql, int from)
    {
        for (var index = from; index < sql.Length; index++)
        {
            if (IsWordAt(sql, index, "SELECT TOP") && sql.AsSpan(index + "SELECT TOP".Length).StartsWith(" ("))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Where the scope of a SELECT ends: at the parenthesis that closes it, or at the end of the text. A set operation inside a sliced scope is not read.</summary>
    private static int ScopeEnd(string sql, int number, int start)
    {
        var depth = 0;
        for (var index = start; index < sql.Length; index++)
        {
            switch (sql[index])
            {
                case '\'':
                    index = ClosingQuote(sql, number, index);
                    continue;
                case '(':
                    depth++;
                    continue;
                case ')':
                    if (depth == 0) return index;
                    depth--;
                    continue;
            }

            if (depth == 0 && IsWordAt(sql, index, "UNION"))
            {
                throw new InvalidDataException($"Query {number}: a sliced SELECT is a member of a set operation, which the rule of decision 170 does not read.");
            }
        }

        return sql.Length;
    }

    /// <summary>The clauses of the scope of one SELECT, read at its own depth of parentheses.</summary>
    private static ScopeClauses ReadScope(string sql, int number, int start, int end)
    {
        var listStart = start + "SELECT ".Length;
        if (IsWordAt(sql, listStart, "TOP"))
        {
            listStart = sql.IndexOf(')', listStart) + 2;
        }

        if (IsWordAt(sql, listStart, "DISTINCT"))
        {
            listStart += "DISTINCT ".Length;
        }

        int from = -1, groupBy = -1, having = -1, orderBy = -1, where = -1;
        var depth = 0;
        for (var index = listStart; index < end; index++)
        {
            switch (sql[index])
            {
                case '\'':
                    index = ClosingQuote(sql, number, index);
                    continue;
                case '(':
                    depth++;
                    continue;
                case ')':
                    depth--;
                    continue;
            }

            if (depth != 0)
            {
                continue;
            }

            if (from < 0 && IsWordAt(sql, index, "FROM")) from = index;
            else if (IsWordAt(sql, index, "WHERE")) where = index;
            else if (IsWordAt(sql, index, "GROUP BY")) groupBy = index;
            else if (IsWordAt(sql, index, "HAVING")) having = index;
            else if (IsWordAt(sql, index, "ORDER BY")) orderBy = index;
        }

        if (from < 0)
        {
            throw new InvalidDataException($"Query {number}: the projection has no FROM.");
        }

        var projection = SplitItems(sql, number, listStart, from).Select(ProjectedItemOf).ToList();
        if (projection.Any(item => WholeRow.IsMatch(item.Expression)))
        {
            throw new InvalidDataException($"Query {number}: a sliced scope projects the whole row, which no ordering written from the text could determine.");
        }

        var groupEnd = having >= 0 ? having : orderBy >= 0 ? orderBy : end;
        var group = groupBy < 0 ? [] : SplitItems(sql, number, groupBy + "GROUP BY".Length, groupEnd);
        var order = orderBy < 0 ? [] : SplitItems(sql, number, orderBy + "ORDER BY".Length, end);

        return new ScopeClauses(projection, group, order, end);
    }

    private static ProjectedItem ProjectedItemOf(string item)
    {
        if (Alias.Match(item) is { Success: true } alias)
        {
            return new ProjectedItem(item[..alias.Index].TrimEnd(), alias.Groups[1].Value);
        }

        if (Reference.Match(item) is { Success: true } reference)
        {
            return new ProjectedItem(item, reference.Groups[1].Value);
        }

        if (!item.EndsWith(')') && TrailingName.Match(item) is { Success: true } trailing)
        {
            return new ProjectedItem(item[..(trailing.Index + 1)].TrimEnd(), trailing.Groups[1].Value);
        }

        return new ProjectedItem(item, null);
    }

    /// <summary>An ordering key that names a projected item stands for the item's expression; any other key is its own expression.</summary>
    private static string Resolved(string key, List<ProjectedItem> projection)
    {
        var named = projection.FirstOrDefault(item => item.Name is not null && item.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(key, @"^\w+$"));
        return Normalized(named?.Expression ?? key);
    }

    /// <summary>Two expressions are the same when they read alike: case and whitespace aside.</summary>
    private static string Normalized(string expression) => Whitespace.Replace(expression, string.Empty).ToUpperInvariant();

    /// <summary>The items of a comma-separated list between two positions, split at the depth of the list.</summary>
    private static List<string> SplitItems(string sql, int number, int start, int end)
    {
        var items = new List<string>();
        var itemStart = start;
        var depth = 0;
        for (var index = start; index < end; index++)
        {
            switch (sql[index])
            {
                case '\'':
                    index = ClosingQuote(sql, number, index);
                    continue;
                case '(':
                    depth++;
                    continue;
                case ')':
                    depth--;
                    continue;
                case ',' when depth == 0:
                    items.Add(sql[itemStart..index].Trim());
                    itemStart = index + 1;
                    continue;
            }
        }

        items.Add(sql[itemStart..end].Trim());
        return items;
    }

    private static int ClosingQuote(string sql, int number, int opening)
    {
        var closing = sql.IndexOf('\'', opening + 1);
        return closing < 0 ? throw new InvalidDataException($"Query {number}: an unclosed string literal.") : closing;
    }

    private static readonly Regex Alias = new(@"\s+AS\s+(\w+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Reference = new(@"^(?:\w+\.)?(\w+)$", RegexOptions.Compiled);
    private static readonly Regex TrailingName = new(@"\)\s+(\w+)$", RegexOptions.Compiled);

    private static string NameProjection(string sql, int number, TextWriter log, out IReadOnlyList<string> columns)
    {
        // The main query starts after the definitions of a WITH: at the SELECT that follows the
        // last closing parenthesis at depth 0 before any SELECT at depth 0.
        var main = 0;
        if (sql.StartsWith("WITH ", StringComparison.Ordinal))
        {
            var depth = 0;
            for (var index = 0; index < sql.Length; index++)
            {
                if (sql[index] == '(') depth++;
                else if (sql[index] == ')') depth--;
                else if (depth == 0 && IsWord(sql, index, "SELECT"))
                {
                    main = index;
                    break;
                }
            }
        }

        var listStart = main + "SELECT ".Length;
        if (IsWordAt(sql, listStart, "TOP"))
        {
            listStart = sql.IndexOf(')', listStart) + 2;
        }

        var listEnd = -1;
        var items = new List<(int Start, int End)>();
        var itemStart = listStart;
        var level = 0;
        for (var index = listStart; index < sql.Length; index++)
        {
            var character = sql[index];
            if (character == '\'')
            {
                var closing = sql.IndexOf('\'', index + 1);
                index = closing < 0 ? throw new InvalidDataException($"Query {number}: an unclosed string literal.") : closing;
                continue;
            }

            if (character == '(') level++;
            else if (character == ')') level--;
            else if (level == 0 && character == ',' && listEnd < 0)
            {
                items.Add((itemStart, index));
                itemStart = index + 1;
            }
            else if (level == 0 && listEnd < 0 && IsWordAt(sql, index, "FROM"))
            {
                items.Add((itemStart, index));
                listEnd = index;
                break;
            }
        }

        if (listEnd < 0)
        {
            throw new InvalidDataException($"Query {number}: the projection has no FROM.");
        }

        var names = new List<string>();
        var insertions = new List<(int Position, string Text)>();
        foreach (var (start, end) in items)
        {
            var item = sql[start..end].Trim();
            if (Alias.Match(item) is { Success: true } alias)
            {
                names.Add(alias.Groups[1].Value);
            }
            else if (Reference.Match(item) is { Success: true } reference)
            {
                names.Add(reference.Groups[1].Value);
            }
            else if (!item.EndsWith(')') && TrailingName.Match(item) is { Success: true } trailing)
            {
                names.Add(trailing.Groups[1].Value);
            }
            else
            {
                var name = $"AggResult{insertions.Count + 1}";
                names.Add(name);
                insertions.Add((start + sql[start..end].TrimEnd().Length, $" AS {name}"));
            }
        }

        var text = new StringBuilder(sql);
        foreach (var (position, insertion) in insertions.OrderByDescending(entry => entry.Position))
        {
            text.Insert(position, insertion);
        }

        if (insertions.Count > 0)
        {
            log.WriteLine($"q{number}: {insertions.Count} unnamed aggregate(s) named.");
        }

        if (names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException($"Query {number}: two projected columns share a name: {string.Join(", ", names)}.");
        }

        columns = names;
        return text.ToString();
    }

    /// <summary>
    /// A space after the closing quote of a string literal that a letter follows. Written as a
    /// scan over the quotes rather than a regular expression: an expression over '…' can match
    /// from the closing quote of one literal to the opening quote of the next and put the space
    /// inside the next literal, which is what the first two attempts did.
    /// </summary>
    private static string SeparateLiterals(string sql)
    {
        var text = new StringBuilder(sql.Length + 16);
        var inside = false;

        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            text.Append(character);
            if (character != '\'')
            {
                continue;
            }

            inside = !inside;
            if (!inside && index + 1 < sql.Length && char.IsLetter(sql[index + 1]))
            {
                text.Append(' ');
            }
        }

        return text.ToString();
    }

    private static Dictionary<string, Table> Aliases(string sql, Ddl ddl)
    {
        var aliases = new Dictionary<string, Table>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in TableReference.Matches(sql))
        {
            if (ddl.ByName.TryGetValue(match.Groups[1].Value, out var table))
            {
                aliases[match.Groups[2].Success ? match.Groups[2].Value : table.Name] = table;
            }
        }

        return aliases;
    }

    private static List<string> Tables(string sql, Ddl ddl)
        => TableReference.Matches(sql)
            .Select(match => match.Groups[1].Value)
            .Where(ddl.ByName.ContainsKey)
            .Select(name => ddl.ByName[name].Name)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string MoveRowLimits(string sql, int number, TextWriter log)
    {
        var fetches = FetchFirst.Matches(sql);
        if (fetches.Count == 0)
        {
            return sql;
        }

        var edits = new List<(int Position, int Length, string Text)>();
        var depth = 0;
        var lastSelect = new Dictionary<int, int>();
        var next = 0;

        for (var index = 0; index < sql.Length; index++)
        {
            if (next < fetches.Count && fetches[next].Index == index)
            {
                var fetch = fetches[next++];
                if (!lastSelect.TryGetValue(depth, out var select))
                {
                    throw new InvalidDataException($"Query {number}: FETCH FIRST without a SELECT at its depth.");
                }

                edits.Add((fetch.Index, fetch.Length, string.Empty));
                edits.Add((select, 0, $" TOP ({fetch.Groups[1].Value})"));
                index += fetch.Length - 1;
                continue;
            }

            switch (sql[index])
            {
                case '(':
                    depth++;
                    continue;
                case ')':
                    depth--;
                    continue;
                case '\'':
                    var closing = sql.IndexOf('\'', index + 1);
                    index = closing < 0 ? throw new InvalidDataException($"Query {number}: an unclosed string literal.") : closing;
                    continue;
            }

            if (IsWord(sql, index, "SELECT"))
            {
                lastSelect[depth] = index + "SELECT".Length;
                index += "SELECT".Length - 1;
            }
        }

        if (edits.Count > 2)
        {
            log.WriteLine($"q{number}: {edits.Count / 2} row limits moved.");
        }

        var text = new StringBuilder(sql);
        foreach (var (position, length, replacement) in edits.OrderByDescending(edit => edit.Position).ThenByDescending(edit => edit.Length))
        {
            text.Remove(position, length);
            text.Insert(position, replacement);
        }

        return text.ToString();
    }

    private static bool IsWord(string sql, int index, string word)
        => string.CompareOrdinal(sql, index, word, 0, word.Length) == 0
            && (index == 0 || !char.IsLetterOrDigit(sql[index - 1]))
            && (index + word.Length == sql.Length || !char.IsLetterOrDigit(sql[index + word.Length]));

    private static readonly string[] ClauseStarts = ["FROM", "INNER JOIN", "LEFT OUTER JOIN", "LEFT JOIN", "WHERE", "GROUP BY", "HAVING", "ORDER BY", "UNION ALL", "UNION"];

    /// <summary>
    /// Lines and indentation for a text the benchmark writes on one line: a clause starts a
    /// line, a subquery is indented one level, a conjunction of WHERE or HAVING continues on
    /// its own line. Only the look changes; the tokens stay as they are.
    /// </summary>
    private static string Format(string sql)
    {
        var text = new StringBuilder();
        var depth = 0;
        var index = 0;
        var inString = false;
        var sinceBetween = false;

        string Indent() => new(' ', depth * 4);

        while (index < sql.Length)
        {
            var character = sql[index];

            if (inString)
            {
                text.Append(character);
                if (character == '\'')
                {
                    inString = false;
                }

                index++;
                continue;
            }

            if (character == '\'')
            {
                inString = true;
                text.Append(character);
                index++;
                continue;
            }

            if (character == '(' && IsWordAt(sql, index + 1, "SELECT"))
            {
                depth++;
                text.Append('(').AppendLine().Append(Indent());
                index++;
                SkipSpaces(sql, ref index);
                continue;
            }

            if (character == '(' )
            {
                depth++;
                text.Append('(');
                index++;
                continue;
            }

            if (character == ')')
            {
                depth--;
                if (text.Length > 0 && EndsClause(text))
                {
                    TrimEnd(text);
                    text.AppendLine().Append(Indent());
                }

                text.Append(')');
                index++;
                continue;
            }

            // A SELECT that follows a set operator or the definitions of a WITH starts a line.
            var clause = ClauseStarts.FirstOrDefault(start => IsWordAt(sql, index, start))
                ?? (IsWordAt(sql, index, "SELECT") && FollowsSetOperatorOrDefinitions(text) ? "SELECT" : null);
            if (clause is not null)
            {
                TrimEnd(text);
                if (text.Length > 0)
                {
                    text.AppendLine().Append(Indent());
                }

                text.Append(clause);
                index += clause.Length;
                sinceBetween = false;
                continue;
            }

            if (IsWordAt(sql, index, "BETWEEN"))
            {
                sinceBetween = true;
            }

            if (IsWordAt(sql, index, "AND") && !sinceBetween)
            {
                TrimEnd(text);
                text.AppendLine().Append(Indent()).Append("  AND");
                index += 3;
                continue;
            }

            if (IsWordAt(sql, index, "AND"))
            {
                sinceBetween = false;
            }

            text.Append(character);
            index++;
        }

        return text.ToString().Trim();
    }

    private static bool IsWordAt(string sql, int index, string word)
    {
        if (index + word.Length > sql.Length || string.CompareOrdinal(sql, index, word, 0, word.Length) != 0)
        {
            return false;
        }

        return (index == 0 || !char.IsLetterOrDigit(sql[index - 1]) && sql[index - 1] != '_')
            && (index + word.Length == sql.Length || !char.IsLetterOrDigit(sql[index + word.Length]) && sql[index + word.Length] != '_');
    }

    private static bool FollowsSetOperatorOrDefinitions(StringBuilder text)
    {
        var sofar = text.ToString().TrimEnd();
        return sofar.EndsWith("UNION", StringComparison.Ordinal)
            || sofar.EndsWith("UNION ALL", StringComparison.Ordinal)
            || sofar.EndsWith(')');
    }

    private static void SkipSpaces(string sql, ref int index)
    {
        while (index < sql.Length && sql[index] == ' ')
        {
            index++;
        }
    }

    private static void TrimEnd(StringBuilder text)
    {
        while (text.Length > 0 && char.IsWhiteSpace(text[^1]))
        {
            text.Length--;
        }
    }

    /// <summary>Whether the text so far ends inside a multi-line subquery, so its closing parenthesis goes on a line of its own.</summary>
    private static bool EndsClause(StringBuilder text)
    {
        var last = text.ToString().LastIndexOf('(');
        return last >= 0 && text.ToString(last, text.Length - last).Contains('\n');
    }
}
