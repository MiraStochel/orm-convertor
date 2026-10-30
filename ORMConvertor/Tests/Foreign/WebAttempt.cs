using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Model;
using OrmConvertor.Factories;
using SampleData;
using Tests.Differential;

namespace Tests.Foreign;

/// <summary>
/// One cell of the sample of the web channel (decision 189): an exported input - a key and a
/// source -, the target it is translated into and the variant of decision 175, with the number of
/// attempts, three unless the table says otherwise. The table is written by the operator.
/// </summary>
internal sealed record WebCell(ForeignKey Key, ORMEnum Source, ORMEnum Target, string Variant, int Attempts)
{
    public const string FileName = "cells.tsv";

    public const int DefaultAttempts = 3;

    /// <summary>
    /// Reads the table: a header naming the columns <c>answers</c>, <c>source</c>, <c>target</c>,
    /// <c>variant</c> and optionally <c>attempts</c>, then a cell per line; empty lines and lines
    /// that start with <c>#</c> are left out. A cell the path could not judge fails by name.
    /// </summary>
    public static List<WebCell> Read(string file)
    {
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"{file} does not exist: the run of the web channel starts with a table of cells - answers, source, target, variant (decision 189).");
        }

        var lines = File.ReadAllLines(file)
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(line => line.Line.Length > 0 && !line.Line.StartsWith('#'))
            .ToList();

        if (lines.Count == 0)
        {
            throw new InvalidOperationException($"{file} holds no header and no cell.");
        }

        var header = lines[0].Line.Split('\t').Select(column => column.Trim().ToLowerInvariant()).ToList();
        int Column(string name, bool required = true)
        {
            var index = header.IndexOf(name);
            return index >= 0 || !required ? index : throw new InvalidOperationException($"{file}: the header names no column \"{name}\"; it names {string.Join(", ", header)}.");
        }

        var answers = Column("answers");
        var source = Column("source");
        var target = Column("target");
        var variant = Column("variant");
        var attempts = Column("attempts", required: false);

        var cells = new List<WebCell>();
        foreach (var (line, number) in lines.Skip(1))
        {
            var values = line.Split('\t').Select(value => value.Trim().Trim('"')).ToList();
            string Value(int index) => index >= 0 && index < values.Count ? values[index] : string.Empty;

            var cell = new WebCell(
                ForeignKey.Parse(Value(answers)),
                Framework(file, number, Value(source)),
                Framework(file, number, Value(target)),
                Value(variant).ToLowerInvariant(),
                Value(attempts).Length == 0 ? DefaultAttempts : int.TryParse(Value(attempts), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0
                    ? count
                    : throw new InvalidOperationException($"{file}, line {number}: \"{Value(attempts)}\" is no number of attempts."));

            if (cell.Source == cell.Target)
            {
                throw new InvalidOperationException($"{file}, line {number}: the cell translates {cell.Source} into itself; a cell translates into another framework.");
            }

            if (!WebTemplate.ForVariant.ContainsKey(cell.Variant))
            {
                throw new InvalidOperationException($"{file}, line {number}: the variant \"{cell.Variant}\" has no template of the web channel; the variants with one are {string.Join(", ", WebTemplate.ForVariant.Keys)} (decision 189).");
            }

            cells.Add(cell);
        }

        return cells;
    }

    private static ORMEnum Framework(string file, int line, string name)
        => Enum.TryParse<ORMEnum>(name, ignoreCase: true, out var framework)
            ? framework
            : throw new InvalidOperationException($"{file}, line {line}: \"{name}\" is no framework of the tool.");
}

/// <summary>
/// A prompt template of the web channel (decision 189): the text of the prompt and, per target,
/// what the answer holds. Embedded in this assembly under its identifier, which goes into the
/// field <c>prompt</c> of every attempt; a template a recorded run used is never changed.
/// </summary>
internal sealed record WebTemplate(string Id, string Prompt, IReadOnlyDictionary<ORMEnum, string> Shapes)
{
    /// <summary>The template of each variant the web channel has one for.</summary>
    public static readonly IReadOnlyDictionary<string, string> ForVariant = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["zero-shot"] = "web-zero-shot-2",
    };

    private static readonly Regex Section = new(@"^=== (?:(prompt)|shape (\w+)) ===$", RegexOptions.CultureInvariant);

    public static WebTemplate Load(string id)
    {
        var name = $"Tests.Foreign.Prompts.{id}.md";
        using var stream = typeof(WebTemplate).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"No prompt template {id}: the templates are Tests/Foreign/Prompts/*.md, embedded as {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(id, reader.ReadToEnd());
    }

    /// <summary>Splits the text into its sections; the lines before the first section are the template's note.</summary>
    public static WebTemplate Parse(string id, string text)
    {
        string? prompt = null;
        var shapes = new Dictionary<ORMEnum, string>();
        string? current = null;
        var lines = new List<string>();

        void Close()
        {
            if (current is null)
            {
                return;
            }

            var body = string.Join('\n', lines).Trim('\n');
            if (current == "prompt")
            {
                prompt = body;
            }
            else
            {
                shapes[Enum.Parse<ORMEnum>(current)] = body;
            }

            lines.Clear();
        }

        foreach (var line in WebPrompt.Lf(text).Split('\n'))
        {
            var match = Section.Match(line);
            if (match.Success)
            {
                Close();
                current = match.Groups[1].Success ? "prompt" : match.Groups[2].Value;
                if (current != "prompt" && !Enum.TryParse<ORMEnum>(current, out _))
                {
                    throw new InvalidOperationException($"The template {id} has a shape of \"{current}\", which is no framework of the tool.");
                }

                continue;
            }

            if (current is not null)
            {
                lines.Add(line);
            }
        }

        Close();

        return new WebTemplate(
            id,
            prompt ?? throw new InvalidOperationException($"The template {id} has no section \"=== prompt ===\"."),
            shapes);
    }
}

/// <summary>
/// The prompt of one cell (decision 189): the template filled with the source and the target, their
/// pinned versions, the dialect, what the answer holds, how the query is called, and the exported
/// input of the cell as files - nothing that leads to the repository: no key, no name of the tool,
/// no path. The marker of an excerpt (decision 138) is the one place an input names the tool, and
/// it goes into the prompt neutral. The text is the same bytes on every machine, whatever line
/// endings the checkout gave the inputs, so that its digest identifies it.
/// </summary>
internal static class WebPrompt
{
    /// <summary>The names a reply may not carry and a prompt never does: the repository, the tool, their authors (decision 189).</summary>
    public static readonly IReadOnlyList<string> TraceNames = ["orm-convertor", "ormconvertor", "ormorpher", "mirastochel", "milan252525"];

    /// <summary>The marker an excerpt of a real project puts on what it changed (decision 138), and the neutral word that stands for it in a prompt.</summary>
    public const string ExcerptMarker = "ORMConvertor:";

    public const string NeutralMarker = "Excerpt note:";

    private static readonly Regex Slot = new(@"\{\{[A-Z_]+\}\}", RegexOptions.CultureInvariant);

    /// <summary>The roles a file of an answer may have in each target, in the order the prompt names them.</summary>
    public static IReadOnlyList<ConversionContentType> Roles(ORMEnum target) => target switch
    {
        ORMEnum.Dapper => [ConversionContentType.CSharpEntity, ConversionContentType.CSharpQuery],
        ORMEnum.EFCore => [ConversionContentType.CSharpEntity, ConversionContentType.CSharpQuery, ConversionContentType.SqlQuery],
        ORMEnum.NHibernate => [ConversionContentType.CSharpEntity, ConversionContentType.XML, ConversionContentType.CSharpQuery, ConversionContentType.HqlQuery, ConversionContentType.CSharpLinqQuery, ConversionContentType.SqlQuery],
        ORMEnum.Hibernate or ORMEnum.EclipseLink => [ConversionContentType.JavaEntity, ConversionContentType.JavaQuery, ConversionContentType.JpqlQuery, ConversionContentType.SqlQuery],
        ORMEnum.MyBatis => [ConversionContentType.JavaEntity, ConversionContentType.XML, ConversionContentType.JavaQuery],
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    public static string DisplayName(ORMEnum framework) => framework switch
    {
        ORMEnum.Dapper => "Dapper",
        ORMEnum.EFCore => "Entity Framework Core",
        ORMEnum.NHibernate => "NHibernate",
        ORMEnum.Hibernate => "Hibernate ORM",
        ORMEnum.EclipseLink => "EclipseLink",
        ORMEnum.MyBatis => "MyBatis",
        _ => framework.ToString(),
    };

    /// <summary>Renders the prompt of the input in the directory, translated into the target.</summary>
    public static string Render(WebTemplate template, string inputDirectory, ORMEnum target)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(inputDirectory, ForeignInputs.ManifestFileName)));
        var root = manifest.RootElement;
        var answers = root.GetProperty("answers").GetString()!;
        var key = ForeignKey.Parse(answers);
        var source = Enum.Parse<ORMEnum>(root.GetProperty("source").GetString()!);
        var questions = root.TryGetProperty("questions", out var questionElement)
            ? questionElement.EnumerateArray().Select(question => question.GetString() ?? string.Empty).ToList()
            : [];
        var units = root.GetProperty("units").EnumerateArray()
            .Select(unit => unit.GetProperty("path").GetString()!)
            .Select(path => (Path: path, Content: Neutral(Lf(File.ReadAllText(Path.Combine(inputDirectory, path))))))
            .ToList();

        var shape = template.Shapes.TryGetValue(target, out var described)
            ? described
            : throw new InvalidOperationException($"The template {template.Id} says nothing of what an answer for {target} holds.");

        var targetDescriptor = DescriptorFactory.Create(target);
        var text = template.Prompt
            .Replace("{{SOURCE}}", DisplayName(source), StringComparison.Ordinal)
            .Replace("{{SOURCE_VERSION}}", DescriptorFactory.Create(source).Version, StringComparison.Ordinal)
            .Replace("{{TARGET}}", DisplayName(target), StringComparison.Ordinal)
            .Replace("{{TARGET_VERSION}}", targetDescriptor.Version, StringComparison.Ordinal)
            .Replace("{{DIALECT}}", Dialect(targetDescriptor.Dialect), StringComparison.Ordinal)
            .Replace("{{ROLES}}", Enumerate(Roles(target).Select(role => $"`{role}`")), StringComparison.Ordinal)
            .Replace("{{CALL}}", Call(key, target), StringComparison.Ordinal)
            .Replace("{{SHAPE}}", shape, StringComparison.Ordinal);

        // The input goes in last, so that nothing in it is ever read as a slot.
        var slot = text.IndexOf("{{INPUT}}", StringComparison.Ordinal);
        if (slot < 0)
        {
            throw new InvalidOperationException($"The template {template.Id} has no slot for the input.");
        }

        var leftover = Slot.Match(text.Remove(slot, "{{INPUT}}".Length));
        if (leftover.Success)
        {
            throw new InvalidOperationException($"The template {template.Id} has the slot {leftover.Value}, which nothing fills.");
        }

        var prompt = text[..slot] + Input(questions, units) + text[(slot + "{{INPUT}}".Length)..];
        prompt = prompt.TrimEnd('\n') + "\n";

        var trace = TraceNames.FirstOrDefault(name => prompt.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (trace is not null)
        {
            throw new InvalidOperationException($"The prompt of {answers} from {source} into {target} would name \"{trace}\", which leads to the repository; no prompt names it (decision 189).");
        }

        if (prompt.Contains(answers, StringComparison.OrdinalIgnoreCase) || prompt.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(inputDirectory)), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The prompt of {answers} from {source} into {target} would carry the key of its cell; no prompt does (decision 189).");
        }

        return prompt;
    }

    /// <summary>The digest of a prompt that the attempt records (<c>promptSha256</c>).</summary>
    public static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("﻿", string.Empty, StringComparison.Ordinal);

    /// <summary>The input with the marker of an excerpt in its neutral word; nothing else of the input changes.</summary>
    public static string Neutral(string content) => content.Replace(ExcerptMarker, NeutralMarker, StringComparison.Ordinal);

    /// <summary>A fence longer than every run of backticks in the content.</summary>
    public static string Fence(string content)
    {
        var longest = Regex.Matches(content, "`+").Select(match => match.Length).DefaultIfEmpty(0).Max();
        return new string('`', Math.Max(3, longest + 1));
    }

    private static string Dialect(DatabaseDialect dialect) => dialect switch
    {
        DatabaseDialect.SqlServer2022 => "Microsoft SQL Server 2022, and the SQL is its T-SQL",
        _ => dialect.ToString(),
    };

    private static string Input(List<string> questions, List<(string Path, string Content)> units)
    {
        var text = new StringBuilder();

        if (questions.Count > 0)
        {
            text.Append("The query answers this question: \"").Append(questions[0]).Append("\"\n");
            if (questions.Count > 1)
            {
                text.Append("It is also asked as: ").Append(string.Join("; ", questions.Skip(1).Select(question => $"\"{question}\""))).Append(".\n");
            }

            text.Append('\n');
        }

        foreach (var (path, content) in units)
        {
            var fence = Fence(content);
            text.Append("FILE: ").Append(path).Append('\n')
                .Append(fence).Append(Language(path)).Append('\n')
                .Append(content.TrimEnd('\n')).Append('\n')
                .Append(fence).Append("\n\n");
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string Language(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".cs" => "csharp",
        ".java" => "java",
        ".xml" => "xml",
        ".sql" => "sql",
        ".hql" => "hql",
        ".jpql" => "jpql",
        _ => "text",
    };

    /// <summary>
    /// How the caller calls the query (decision 189): the handle the target hands it, the arguments
    /// in the order the caller binds them, the fields it reads a row by and whether a row is an
    /// entity or a projection - for a key whose query runs at the fourth level against a measure
    /// (the matrix, the FIBEN catalog). The contract of the consumer project, not the answer.
    /// </summary>
    private static string Call(ForeignKey key, ORMEnum target)
    {
        var handle = target switch
        {
            ORMEnum.Dapper => "the open connection (`IDbConnection connection`)",
            ORMEnum.EFCore => "the context (`DbContext ctx`)",
            ORMEnum.NHibernate => "the open session (`ISession session`)",
            ORMEnum.Hibernate or ORMEnum.EclipseLink => "the entity manager (`EntityManager em`)",
            _ => null,
        };

        (IReadOnlyList<(string Name, string Type)> Arguments, IReadOnlyList<string> Fields, bool Projection)? contract = key.Kind switch
        {
            ForeignKey.Matrix => DifferentialMatrix.Queries.SingleOrDefault(query => query.Id == key.Id) is { } query
                ? ([.. query.Arguments.Select(argument => (argument.Name, argument.TypeName))], query.Fields, query.Projection)
                : null,
            ForeignKey.Fiben => FibenSample.Queries.SingleOrDefault(query => query.Key.Equals(key.Id, StringComparison.OrdinalIgnoreCase)) is { } fiben
                ? ([], fiben.Columns, true)
                : null,
            _ => null,
        };

        if (contract is not { } call)
        {
            return handle is null
                ? "Each mapper method takes one parameter for each parameter of its source query, under the name the source gives it. The caller compiles the translation and lets the framework accept it."
                : $"Each query method takes {handle} first and then one parameter for each parameter of its source query, under the name the source gives it. The caller compiles the translation and lets the framework accept it.";
        }

        var arguments = call.Arguments.Count == 0
            ? null
            : Enumerate(call.Arguments.Select(argument => $"`{argument.Name}` ({TypeWords(argument.Type)})"));

        var text = new StringBuilder();
        text.Append(handle is null
            ? arguments is null ? "The caller calls the mapper method with no argument." : $"The caller calls the mapper method with exactly these arguments, in this order: {arguments}."
            : arguments is null ? $"The caller passes {handle} and no other argument." : $"The caller passes {handle} first and then exactly these arguments, in this order: {arguments}.");

        text.Append(" It reads every row of the result by these fields: ").Append(string.Join(", ", call.Fields.Select(field => $"`{field}`"))).Append(". ");
        text.Append(call.Projection
            ? target switch
            {
                ORMEnum.Dapper => "The query returns a projection: each row is a `dynamic` row whose columns carry these names.",
                ORMEnum.EFCore => "The query returns a projection: each element is an object - an anonymous type or a class - whose members carry these names.",
                ORMEnum.NHibernate => "The query returns a projection: in HQL the `select` clause lists these fields in this order and each row comes back as an array of them; in the LINQ form each element is an object whose members carry these names.",
                ORMEnum.MyBatis => "The query returns a projection: each row is a map (`resultType=\"map\"`) whose keys are these column labels.",
                _ => "The query returns a projection: the `select` clause lists these fields in this order and each row comes back as an `Object[]` of them.",
            }
            : "The query returns whole entities: each row is an instance of the entity class, read by its properties.");
        text.Append(" Its rows are compared with the rows the source query returns over the same data.");

        return text.ToString();
    }

    private static string TypeWords(string type) => type switch
    {
        "int" => "a 32-bit integer",
        "long" => "a 64-bit integer",
        "decimal" => "a decimal number",
        "string" => "a string",
        "int[]" => "a collection of 32-bit integers",
        _ => type,
    };

    private static string Enumerate(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count switch
        {
            0 => string.Empty,
            1 => list[0],
            _ => string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1],
        };
    }
}

/// <summary>One file of a reply: the path and the role its line states, the content of the block below it, and whether the reply ended inside the block.</summary>
internal sealed record WebReplyFile(string Path, string Role, string Content, bool Cut);

/// <summary>
/// A reply of the web channel read as the prompt asked for it (decision 189): every file a line
/// <c>FILE: &lt;path&gt; | &lt;role&gt;</c> and the first fenced block below it, the report the
/// fenced block below <c>REPORT:</c>. Markdown emphasis around the two lines is tolerated, since a
/// copy of a rendered reply may keep it; anything else is read as written. Text outside the
/// blocks of files is kept, because that is where a link to a source would stand.
/// </summary>
internal sealed class WebReply
{
    private static readonly Regex FenceOpen = new(@"^\s*(`{3,}|~{3,})", RegexOptions.CultureInvariant);

    public List<WebReplyFile> Files { get; } = [];

    /// <summary>The text of the report block, or null where the reply carries none.</summary>
    public string? Report { get; private set; }

    /// <summary>Everything outside the blocks of files: prose, the marker lines, the report, any other block.</summary>
    public string Outside { get; private set; } = string.Empty;

    /// <summary>Why the reply cannot be read as files, or null.</summary>
    public string? Unreadable { get; private set; }

    public static WebReply Parse(string text)
    {
        var reply = new WebReply();
        var lines = WebPrompt.Lf(text).Split('\n');
        var outside = new StringBuilder();

        int NextNonBlank(int index)
        {
            while (index < lines.Length && lines[index].Trim().Length == 0)
            {
                index++;
            }

            return index;
        }

        var position = 0;
        while (position < lines.Length)
        {
            var line = lines[position];
            var (marker, rest) = Marker(line);

            if (marker == "FILE")
            {
                outside.Append(line).Append('\n');
                var bar = rest.IndexOf('|');
                var path = (bar < 0 ? rest : rest[..bar]).Trim();
                var role = bar < 0 ? string.Empty : rest[(bar + 1)..].Trim();
                var block = NextNonBlank(position + 1);

                if (path.Length == 0 || role.Length == 0)
                {
                    reply.Unreadable ??= $"the line \"{line.Trim()}\" states no {(path.Length == 0 ? "file name" : "role")}";
                    position++;
                    continue;
                }

                if (block >= lines.Length || !FenceOpen.IsMatch(lines[block]) || Marker(lines[block]).Marker is not null)
                {
                    reply.Unreadable ??= $"the file {path} is not followed by a code block";
                    position++;
                    continue;
                }

                var (content, end, cut) = Block(lines, block);
                reply.Files.Add(new WebReplyFile(path.Replace('\\', '/'), role, content, cut));
                position = end;
                continue;
            }

            if (marker == "REPORT")
            {
                outside.Append(line).Append('\n');
                var block = NextNonBlank(position + 1);
                if (block < lines.Length && FenceOpen.IsMatch(lines[block]))
                {
                    var (content, end, _) = Block(lines, block);
                    reply.Report = content;
                    outside.Append(content).Append('\n');
                    position = end;
                    continue;
                }

                position++;
                continue;
            }

            if (FenceOpen.IsMatch(line))
            {
                // A block below no FILE line is no file of the artifact; it stays outside.
                var (content, end, _) = Block(lines, position);
                outside.Append(content).Append('\n');
                position = end;
                continue;
            }

            outside.Append(line).Append('\n');
            position++;
        }

        reply.Outside = outside.ToString();
        return reply;
    }

    /// <summary>The marker a line is - FILE or REPORT - with the rest of the line, read past the emphasis, a heading or a quote a rendered copy may add.</summary>
    private static (string? Marker, string After) Marker(string line)
    {
        var stripped = line.Replace("*", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal).Trim().TrimStart('#', '>', '-', ' ', '\t');
        foreach (var marker in (string[])["FILE", "REPORT"])
        {
            if (stripped.StartsWith(marker + ":", StringComparison.OrdinalIgnoreCase))
            {
                return (marker, stripped[(marker.Length + 1)..]);
            }
        }

        return (null, string.Empty);
    }

    /// <summary>The content of the fenced block that opens at the line, the index past its closing fence, and whether the reply ended before it closed.</summary>
    private static (string Content, int End, bool Cut) Block(string[] lines, int open)
    {
        var fence = FenceOpen.Match(lines[open]).Groups[1].Value;
        var closing = new Regex($@"^\s*{Regex.Escape(fence[..1])}{{{fence.Length},}}\s*$", RegexOptions.CultureInvariant);

        var content = new List<string>();
        for (var index = open + 1; index < lines.Length; index++)
        {
            if (closing.IsMatch(lines[index]))
            {
                return (string.Join('\n', content), index + 1, false);
            }

            content.Add(lines[index]);
        }

        return (string.Join('\n', content).TrimEnd('\n'), lines.Length, true);
    }
}

/// <summary>
/// The fourth layer of the ban on the web (decision 189): the trace of a search a reply carries -
/// a name of the repository, the tool or their authors anywhere, a link outside the blocks of
/// files, a tool, a source or a context from outside the conversation (a memory, a custom
/// instruction, a project, a file) the report declares. A link inside a file - the namespace of
/// an XML document, the DTD of a MyBatis mapper - is no trace.
/// </summary>
internal static class WebTrace
{
    private static readonly Regex Link = new(@"https?://\S+|\bwww\.\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> Nothing = new(["", "none", "n/a", "no", "nothing", "null"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The trace the reply carries, or null.</summary>
    public static string? Find(WebReply reply, string text)
    {
        var name = WebPrompt.TraceNames.FirstOrDefault(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (name is not null)
        {
            return $"the reply names \"{name}\"";
        }

        var link = Link.Match(reply.Outside);
        if (link.Success)
        {
            return $"the reply carries a link outside its files: {link.Value}";
        }

        if (reply.Report is not null && TryParse(reply.Report) is JsonObject report)
        {
            foreach (var field in (string[])["toolsUsed", "sourcesConsulted", "contextUsed"])
            {
                var declared = Declared(report[field]);
                if (declared.Count > 0)
                {
                    return $"the report declares {field}: {string.Join(", ", declared)}";
                }
            }
        }

        return null;
    }

    public static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> Declared(JsonNode? node) => node switch
    {
        JsonArray array => [.. array.Select(item => item?.ToString() ?? string.Empty).Where(item => !Nothing.Contains(item.Trim()))],
        JsonValue value when value.ToString() is { } single && !Nothing.Contains(single.Trim()) && single != "false" => [single],
        _ => [],
    };
}

/// <summary>
/// The run of the web channel (decision 189) in one directory: the operator's table of cells, the
/// inputs of the sample, a directory per attempt with its prompt and - once the operator has made
/// the attempt - its reply, the table of attempts with the operator's measurements, the artifacts
/// the import writes, and the rows of the attempts that gave none. The experiment run (decision
/// 173) and the run over foreign artifacts (decision 172) write their rows into the same
/// directory, and the summary pairs them.
/// </summary>
internal static class WebAttempts
{
    public const string RunVariable = "ORMCONVERTOR_LLM_RUN";

    public const string AttemptsFileName = "attempts.tsv";

    /// <summary>The rows of the attempts whose reply gave no artifact - level 0, as the tool's refusal is (decision 173); the summary reads them with the rows of foreign artifacts.</summary>
    public const string RowsFileName = "attempt-rows.tsv";

    public const string PromptFileName = "prompt.md";

    public const string ReplyFileName = "reply.md";

    public const string Producer = "llm";

    /// <summary>The columns of the table of attempts: those the generator fills, then those the operator fills.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "id", "answers", "source", "target", "variant", "attempt", "prompt", "promptSha256",
        "model", "version", "channel", "frontend", "temperature", "seconds", "tokensIn", "tokensOut", "costUsd", "credits", "creditsUnit", "note",
    ];

    private static readonly string[] Numbers = ["seconds", "tokensIn", "tokensOut", "costUsd", "credits"];

    private static readonly string[] Channels = ["console", "chat"];

    public static string? RunDirectory
        => Environment.GetEnvironmentVariable(RunVariable) is { } directory && !string.IsNullOrWhiteSpace(directory) ? directory.Trim() : null;

    public static string AttemptDirectory(string run, string id) => Path.Combine(run, "attempts", id);

    /// <summary>
    /// Writes the inputs of the sample under <c>inputs/</c>, a prompt per attempt and the table of
    /// attempts, keeping what the operator already wrote into it. An attempt that has a reply keeps
    /// its prompt: a different prompt for it fails by name. Returns the identifiers of the attempts.
    /// </summary>
    public static List<string> Prepare(string run)
    {
        var cells = WebCell.Read(Path.Combine(run, WebCell.FileName));
        var inputs = Path.Combine(run, "inputs");

        var wanted = cells.Select(cell => Of(cell.Key, cell.Source)).ToHashSet();
        var exported = new Dictionary<(string Kind, string Id, ORMEnum Source), ForeignKey>();
        ForeignInputs.Export(inputs, (key, source) =>
        {
            if (!wanted.Contains(Of(key, source)))
            {
                return false;
            }

            exported[Of(key, source)] = key;
            return true;
        });

        var table = Path.Combine(run, AttemptsFileName);
        var rows = File.Exists(table) ? ReadTable(table).ToDictionary(row => row["id"], StringComparer.Ordinal) : new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var ids = new List<string>();

        foreach (var cell in cells)
        {
            var key = exported.TryGetValue(Of(cell.Key, cell.Source), out var found)
                ? found
                : throw new InvalidOperationException($"The cell {cell.Key} from {cell.Source} has no input: the export writes no input of this key from this source (decision 172).");

            var template = WebTemplate.Load(WebTemplate.ForVariant[cell.Variant]);
            var inputDirectory = ForeignInputs.DirectoryOf(inputs, key, cell.Source);
            var prompt = WebPrompt.Render(template, inputDirectory, cell.Target);
            var digest = WebPrompt.Digest(prompt);

            for (var attempt = 1; attempt <= cell.Attempts; attempt++)
            {
                var id = $"{Path.GetFileName(inputDirectory)}-{cell.Target}-{cell.Variant}-{attempt}";
                var directory = AttemptDirectory(run, id);

                if (rows.TryGetValue(id, out var existing) && existing.GetValueOrDefault("promptSha256") is { Length: > 0 } before && before != digest
                    && File.Exists(Path.Combine(directory, ReplyFileName)))
                {
                    throw new InvalidOperationException($"{id} has a reply to another prompt than the one the template {template.Id} gives now; a template a run used does not change (decision 189) - make a new template, or move the attempt away.");
                }

                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, PromptFileName), prompt, new UTF8Encoding(false));

                var row = existing ?? Columns.ToDictionary(column => column, _ => string.Empty, StringComparer.Ordinal);
                row["id"] = id;
                row["answers"] = key.ToString();
                row["source"] = cell.Source.ToString();
                row["target"] = cell.Target.ToString();
                row["variant"] = cell.Variant;
                row["attempt"] = attempt.ToString(CultureInfo.InvariantCulture);
                row["prompt"] = template.Id;
                row["promptSha256"] = digest;
                rows[id] = row;
                ids.Add(id);
            }
        }

        WriteTable(table, rows.Values);
        return ids;
    }

    /// <summary>What an import did, attempt by attempt.</summary>
    internal sealed record ImportReport(List<string> Imported, List<string> WithoutArtifact, List<(string Id, string Trace)> Refused, List<string> Pending, List<string> Problems);

    /// <summary>
    /// Imports every attempt that has a reply: a reply with a trace of a search is refused - the
    /// reply moves to <c>refused-&lt;n&gt;.md</c> beside the reason, and the operator makes the
    /// attempt again in a new conversation -; a reply that gives no artifact the target can read is
    /// a row at level 0; every other reply becomes a directory of a foreign artifact with the
    /// metadata of decision 175 under <c>artifacts/</c>. The rows of level 0 are written whole.
    /// </summary>
    public static ImportReport Import(string run)
    {
        var table = Path.Combine(run, AttemptsFileName);
        if (!File.Exists(table))
        {
            throw new InvalidOperationException($"{table} does not exist: the attempts are prepared first ({nameof(Prepare)}).");
        }

        var report = new ImportReport([], [], [], [], []);
        var rows = new List<ForeignVerdict>();
        var artifacts = Path.Combine(run, "artifacts");

        foreach (var row in ReadTable(table))
        {
            var id = row["id"];
            var directory = AttemptDirectory(run, id);
            var replyFile = Path.Combine(directory, ReplyFileName);
            var promptFile = Path.Combine(directory, PromptFileName);
            var artifactDirectory = Path.Combine(artifacts, id);

            if (!File.Exists(replyFile))
            {
                report.Pending.Add(id);
                continue;
            }

            if (!File.Exists(promptFile))
            {
                report.Problems.Add($"{id}: the reply stands without its {PromptFileName}.");
                continue;
            }

            var channel = row.GetValueOrDefault("channel", string.Empty).ToLowerInvariant();
            if (row.GetValueOrDefault("model", string.Empty).Length == 0 || !Channels.Contains(channel))
            {
                report.Problems.Add($"{id}: {AttemptsFileName} states no model, or a channel other than {string.Join(" or ", Channels)}; the operator writes both (decision 189).");
                continue;
            }

            var prompt = File.ReadAllText(promptFile);
            var text = File.ReadAllText(replyFile);
            var reply = WebReply.Parse(text);

            var trace = WebPrompt.Digest(prompt) != row.GetValueOrDefault("promptSha256")
                ? $"the prompt changed after it was generated (its digest is not the one in {AttemptsFileName})"
                : WebTrace.Find(reply, text);

            if (trace is not null)
            {
                var refused = Directory.GetFiles(directory, "refused-*.md").Length + 1;
                File.Move(replyFile, Path.Combine(directory, $"refused-{refused}.md"));
                File.WriteAllText(Path.Combine(directory, $"refused-{refused}.txt"), trace + "\n", new UTF8Encoding(false));
                if (Directory.Exists(artifactDirectory))
                {
                    Directory.Delete(artifactDirectory, recursive: true);
                }

                report.Refused.Add((id, trace));
                continue;
            }

            var key = ForeignKey.Parse(row["answers"]);
            var source = Enum.Parse<ORMEnum>(row["source"]);
            var target = Enum.Parse<ORMEnum>(row["target"]);
            var (units, problem) = Units(reply, target);
            var metadata = Metadata(row, reply, prompt, text, Directory.GetFiles(directory, "refused-*.md").Length);

            if (Directory.Exists(artifactDirectory))
            {
                Directory.Delete(artifactDirectory, recursive: true);
            }

            if (problem is not null)
            {
                rows.Add(new ForeignVerdict
                {
                    Artifact = id,
                    Answers = key.ToString(),
                    Source = source.ToString(),
                    Target = target.ToString(),
                    Producer = Producer,
                    Language = ForeignArtifact.LanguageRefused,
                    Metadata = metadata,
                    Outcome = ForeignVerdict.Refused,
                    Detail = problem,
                });
                report.WithoutArtifact.Add(id);
                continue;
            }

            ForeignArtifact.Write(artifactDirectory, key, source, target, Producer, units, metadata);
            report.Imported.Add(id);
        }

        ForeignRows.Write(run, RowsFileName, rows);
        return report;
    }

    /// <summary>The units of the artifact a reply gives, or why it gives none the target can read: no file, a reply that is not files, a role the target does not have, a path outside the artifact's directory, a file twice.</summary>
    internal static (List<ForeignUnit> Units, string? Problem) Units(WebReply reply, ORMEnum target)
    {
        if (reply.Unreadable is not null)
        {
            return ([], $"the reply cannot be read as files: {reply.Unreadable}");
        }

        if (reply.Files.Count == 0)
        {
            var said = Regex.Replace(reply.Outside, @"\s+", " ").Trim();
            return ([], $"the reply carries no file{(said.Length > 0 ? ": " + (said.Length > 160 ? said[..160] + "…" : said) : string.Empty)}");
        }

        var roles = WebPrompt.Roles(target);
        var units = new List<ForeignUnit>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in reply.Files)
        {
            if (!Enum.TryParse<ConversionContentType>(file.Role, ignoreCase: true, out var role) || !roles.Contains(role))
            {
                return ([], $"{file.Path}: the role \"{file.Role}\" is not one of {target}'s ({string.Join(", ", roles)})");
            }

            var segments = file.Path.Split('/');
            if (Path.IsPathRooted(file.Path) || file.Path.Contains(':', StringComparison.Ordinal) || segments.Any(segment => segment is "" or "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                return ([], $"the file name \"{file.Path}\" is no path inside the artifact's directory");
            }

            if (!paths.Add(file.Path))
            {
                return ([], $"the reply carries the file {file.Path} twice");
            }

            units.Add(new ForeignUnit(file.Path, role, file.Content + "\n"));
        }

        return (units, null);
    }

    /// <summary>
    /// The metadata of an attempt (decision 189): the fields of decision 175 from the generator and
    /// from the operator's measurements, the size of the exchange, the replies refused before this
    /// one, the files the reply ended inside, and the model's own report as it wrote it.
    /// </summary>
    internal static string Metadata(IReadOnlyDictionary<string, string> row, WebReply reply, string prompt, string text, int refusedBefore)
    {
        var metadata = new JsonObject();

        void Text(string field)
        {
            if (row.GetValueOrDefault(field) is { Length: > 0 } value)
            {
                metadata[field] = value;
            }
        }

        void Number(string field)
        {
            if (row.GetValueOrDefault(field) is not { Length: > 0 } value)
            {
                return;
            }

            // A table saved by a spreadsheet in a Czech locale writes a decimal comma.
            var invariant = value.Contains(',', StringComparison.Ordinal) && !value.Contains('.', StringComparison.Ordinal) ? value.Replace(',', '.') : value;
            metadata[field] = decimal.TryParse(invariant, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? JsonValue.Create(number) : JsonValue.Create(value);
        }

        Text("model");
        Text("version");
        Text("channel");
        Text("frontend");

        var channel = row.GetValueOrDefault("channel", string.Empty).ToLowerInvariant();
        if (row.GetValueOrDefault("temperature") is { Length: > 0 })
        {
            Number("temperature");
        }
        else if (channel == "chat")
        {
            // A chat application has no setting for it: the attempt is the second setting of decision 175.
            metadata["temperature"] = "default";
        }

        Text("prompt");
        Text("promptSha256");
        Text("variant");
        metadata["attempt"] = int.Parse(row["attempt"], CultureInfo.InvariantCulture);
        foreach (var field in Numbers)
        {
            Number(field);
        }

        Text("creditsUnit");
        Text("note");
        metadata["promptChars"] = prompt.Length;
        metadata["replyChars"] = text.Length;

        if (refusedBefore > 0)
        {
            metadata["refusedBefore"] = refusedBefore;
        }

        if (reply.Files.Where(file => file.Cut).Select(file => file.Path).ToList() is { Count: > 0 } cut)
        {
            var paths = new JsonArray();
            foreach (var path in cut)
            {
                paths.Add(path);
            }

            metadata["cut"] = paths;
        }

        if (reply.Report is not null)
        {
            metadata["selfReport"] = WebTrace.TryParse(reply.Report) ?? JsonValue.Create(reply.Report);
        }

        return metadata.ToJsonString();
    }

    public static List<Dictionary<string, string>> ReadTable(string file)
    {
        var lines = File.ReadAllLines(file).Where(line => line.Trim().Length > 0).ToList();
        if (lines.Count == 0)
        {
            return [];
        }

        var header = lines[0].Split('\t').Select(column => column.Trim().Trim('"')).ToList();
        if (!header.Contains("id"))
        {
            throw new InvalidOperationException($"{file}: the header names no column \"id\".");
        }

        return [.. lines.Skip(1).Select(line =>
        {
            var values = line.Split('\t');
            var row = Columns.ToDictionary(column => column, _ => string.Empty, StringComparer.Ordinal);
            for (var index = 0; index < header.Count; index++)
            {
                row[header[index]] = index < values.Length ? values[index].Trim().Trim('"') : string.Empty;
            }

            return row;
        })];
    }

    private static void WriteTable(string file, IEnumerable<Dictionary<string, string>> rows)
    {
        var text = new StringBuilder();
        text.Append(string.Join('\t', Columns)).Append('\n');
        foreach (var row in rows.OrderBy(row => row["id"], StringComparer.Ordinal))
        {
            text.Append(string.Join('\t', Columns.Select(column => row.GetValueOrDefault(column, string.Empty).Replace('\t', ' ')))).Append('\n');
        }

        File.WriteAllText(file, text.ToString(), new UTF8Encoding(false));
    }

    private static (string Kind, string Id, ORMEnum Source) Of(ForeignKey key, ORMEnum source) => (key.Kind, key.Id.ToLowerInvariant(), source);
}
