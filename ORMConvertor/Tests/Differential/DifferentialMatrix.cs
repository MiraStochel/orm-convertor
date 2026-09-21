using Model;

namespace Tests.Differential;

/// <summary>One parameter of a query and the value the matrix binds it to (decision 083).</summary>
internal sealed record DifferentialArgument(string Name, string TypeName, string Value)
{
    /// <summary>The value as the generated method's parameter type wants it.</summary>
    public object Materialize() => TypeName switch
    {
        "decimal" => decimal.Parse(Value, System.Globalization.CultureInfo.InvariantCulture),
        "int" => int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture),
        "string" => Value,
        _ => throw new NotSupportedException(
            $"The matrix binds {Name} to a value of type \"{TypeName}\", which no suite knows how "
            + "to make. Add the type to both suites or state the argument differently."),
    };
}

/// <summary>
/// One query of the differential matrix: where it comes from, what a row of its result
/// looks like, and how much of a value survives rendering (decision 089).
/// </summary>
internal sealed record DifferentialQuery(
    string Id,
    ORMEnum Source,
    IReadOnlyList<string> UnitPaths,
    IReadOnlyList<string> Fields,
    bool Projection,
    bool Ordered,
    ResultRow.RenderSettings Settings,
    IReadOnlyList<DifferentialArgument> Arguments,
    IReadOnlyList<string> Mutations)
{
    /// <summary>The canonical result of this query, as the file beside the matrix states it.</summary>
    public List<string> CanonicalResult() => DifferentialData.ReadLines($"results/{Id}.txt");

    /// <summary>
    /// The frameworks this query is paired against: every one but its own source. Its own
    /// run is not a pair - it is what fixes the canonical result - but it happens all the
    /// same, which is how both halves of every pair really run (decision 089).
    /// </summary>
    public IEnumerable<ORMEnum> Targets()
        => Enum.GetValues<ORMEnum>().Where(framework => framework != Source);

    /// <summary>The input units of a conversion, in the order the matrix states them (decision 017).</summary>
    public List<ConversionSource> Units()
        => [.. UnitPaths.Select(path => new ConversionSource
        {
            Name = path[(path.LastIndexOf('/') + 1)..],
            ContentType = ContentTypeOf(path),
            Content = DifferentialData.Read($"inputs/{path}"),
        })];

    /// <summary>
    /// The language a unit is written in, taken from its file name. The table is the Java
    /// suite's <c>ContentType.forFileName</c> and has to stay it: the two suites read the
    /// same files, so a unit that arrived under two different languages would be a finding
    /// about the suites dressed as a finding about the tool.
    /// </summary>
    private static ConversionContentType ContentTypeOf(string path) => path switch
    {
        _ when path.EndsWith(".query.cs", StringComparison.Ordinal) => ConversionContentType.CSharpQuery,
        _ when path.EndsWith(".query.java", StringComparison.Ordinal) => ConversionContentType.JavaQuery,
        _ when path.EndsWith(".cs", StringComparison.Ordinal) => ConversionContentType.CSharpEntity,
        _ when path.EndsWith(".java", StringComparison.Ordinal) => ConversionContentType.JavaEntity,
        _ when path.EndsWith(".xml", StringComparison.Ordinal) => ConversionContentType.XML,
        _ when path.EndsWith(".sql", StringComparison.Ordinal) => ConversionContentType.SqlQuery,
        _ when path.EndsWith(".hql", StringComparison.Ordinal) => ConversionContentType.HqlQuery,
        _ when path.EndsWith(".jpql", StringComparison.Ordinal) => ConversionContentType.JpqlQuery,
        _ => throw new NotSupportedException($"No content type is defined for the extension of \"{path}\"."),
    };
}

/// <summary>
/// Reads <c>matrix.txt</c>. Its counterpart is <c>DifferentialMatrix.java</c>, and the
/// format is poorer than JSON on purpose: two parsers in two languages have to agree about
/// it, and a line of "key = value" cannot be read two ways.
/// </summary>
internal static class DifferentialMatrix
{
    /// <summary>What the criterion of F13 asks of the matrix, asserted by the suite rather than by a document.</summary>
    public const int RequiredPairs = 30;

    private static readonly Lazy<IReadOnlyList<DifferentialQuery>> Cached = new(Parse);

    public static IReadOnlyList<DifferentialQuery> Queries => Cached.Value;

    /// <summary>Every (query, target) the matrix states, which is what a pair of F13 is.</summary>
    public static IEnumerable<(DifferentialQuery Query, ORMEnum Target)> Pairs()
        => Queries.SelectMany(query => query.Targets().Select(target => (query, target)));

    private static IReadOnlyList<DifferentialQuery> Parse()
    {
        var queries = new List<DifferentialQuery>();
        string? id = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        void Flush()
        {
            if (id is not null)
            {
                queries.Add(Build(id, values));
            }

            values = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        foreach (var raw in DifferentialData.ReadLines("matrix.txt"))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                id = line[1..^1].Trim();
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                throw new InvalidOperationException($"matrix.txt: \"{line}\" is neither a section nor a key.");
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        Flush();
        return queries;
    }

    private static DifferentialQuery Build(string id, Dictionary<string, string> values)
    {
        string Required(string key) => values.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"matrix.txt: [{id}] states no \"{key}\".");

        int Number(string key, int fallback)
            => values.TryGetValue(key, out var value)
                ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                : fallback;

        var shape = Required("shape");
        if (shape is not ("entity" or "projection"))
        {
            throw new InvalidOperationException($"matrix.txt: [{id}] has shape \"{shape}\", which is neither.");
        }

        return new DifferentialQuery(
            id,
            Enum.Parse<ORMEnum>(Required("source")),
            List(Required("units")),
            List(Required("fields")),
            shape == "projection",
            bool.Parse(Required("ordered")),
            new ResultRow.RenderSettings(
                Number("decimalScale", 6),
                Number("floatDigits", 12),
                Number("fractionalSeconds", 3)),
            values.TryGetValue("arguments", out var arguments) ? Arguments(arguments) : [],
            List(Required("mutations")));
    }

    private static List<string> List(string value)
        => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static List<DifferentialArgument> Arguments(string value)
        => [.. List(value).Select(entry =>
        {
            var colon = entry.IndexOf(':');
            var equals = entry.IndexOf('=');

            if (colon < 0 || equals < colon)
            {
                throw new InvalidOperationException(
                    $"matrix.txt: the argument \"{entry}\" is not written as name:type=value.");
            }

            return new DifferentialArgument(
                entry[..colon].Trim(),
                entry[(colon + 1)..equals].Trim(),
                entry[(equals + 1)..].Trim());
        })];
}
