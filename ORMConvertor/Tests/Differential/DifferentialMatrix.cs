using AbstractWrappers.Descriptors;
using Model;
using Tests.Combined;
using Tests.Database;

namespace Tests.Differential;

/// <summary>One parameter of a query and the value the matrix binds it to (decision 083).</summary>
internal sealed record DifferentialArgument(string Name, string TypeName, string Value)
{
    /// <summary>
    /// The value as the generated method's parameter type wants it. A collection is written
    /// as its elements separated by semicolons, because the argument list itself is
    /// separated by commas.
    /// </summary>
    public object Materialize() => TypeName switch
    {
        "decimal" => decimal.Parse(Value, System.Globalization.CultureInfo.InvariantCulture),
        "int" => int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture),
        "long" => long.Parse(Value, System.Globalization.CultureInfo.InvariantCulture),
        "string" => Value,
        "int[]" => Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(element => int.Parse(element, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray(),
        _ => throw new NotSupportedException(
            $"The matrix binds {Name} to a value of type \"{TypeName}\", which no suite knows how "
            + "to make. Add the type to both suites or state the argument differently."),
    };
}

/// <summary>
/// One query of the differential matrix: who states it, what a row of its result looks
/// like, and how much of a value survives rendering (decision 089).
///
/// A query has one canonical result and one or more sources. The six queries the matrix
/// began with each have one source of their own; a category of T2 is stated by every source
/// the shared manifest lists for it (<c>QueryShapes/categories.txt</c>), and every one of
/// those source variants runs against the same canonical file - so the comparison is n-way
/// across sources as much as across targets, and a source that read the category into a
/// different query would be caught by its own run before any translation of it is judged.
/// </summary>
/// <param name="Id">The section of <c>matrix.txt</c>, and the name of the canonical result.</param>
/// <param name="Category">The category of the manifest this query is, or null for a query of the matrix's own.</param>
/// <param name="Sources">The frameworks that state the query, in the order the file lists them.</param>
/// <param name="UnitPaths">The input units of a query of the matrix's own, under <c>inputs/</c>; empty for a category.</param>
/// <param name="RefusedBy">Targets that refuse the query, with the feature the refusal names (decision 053).</param>
/// <param name="FallbackBy">Targets that write the query in native SQL because their query language does not speak it, with the feature the record of kind Fallback names (decision 113). They are pairs like any other: the escape path is measured at the fourth level as a translation is.</param>
internal sealed record DifferentialQuery(
    string Id,
    string? Category,
    IReadOnlyList<ORMEnum> Sources,
    IReadOnlyList<string> UnitPaths,
    IReadOnlyList<string> Fields,
    bool Projection,
    bool Ordered,
    ResultRow.RenderSettings Settings,
    IReadOnlyList<DifferentialArgument> Arguments,
    IReadOnlyList<string> Mutations,
    IReadOnlyDictionary<ORMEnum, QueryFeature> RefusedBy,
    IReadOnlyDictionary<ORMEnum, QueryFeature> FallbackBy)
{
    /// <summary>The canonical result of this query, as the file beside the matrix states it.</summary>
    public List<string> CanonicalResult() => DifferentialData.ReadLines($"results/{Id}.txt");

    /// <summary>
    /// The frameworks a source of this query is paired against: every one but the source
    /// itself and but a target that refuses the query - a target that falls back to native
    /// SQL included (decision 113). The source's own
    /// run is not a pair - it is what fixes the canonical result - but it happens all the
    /// same, which is how both halves of every pair really run (decision 089).
    /// </summary>
    public IEnumerable<ORMEnum> Targets(ORMEnum source)
        => Enum.GetValues<ORMEnum>().Where(framework => framework != source && !RefusedBy.ContainsKey(framework));

    /// <summary>
    /// The input units of a conversion from the source, in the order they are sent (decision
    /// 017): for a category the shared domain of seven entities and the category's query units
    /// (<see cref="QueryShapeInputs"/>), for a query of the matrix's own the files the matrix
    /// lists under <c>inputs/</c>, each under the language its file name states.
    /// </summary>
    public List<ConversionSource> Units(ORMEnum source)
    {
        if (!Sources.Contains(source))
        {
            throw new ArgumentException($"{Id} is not stated by {source}.", nameof(source));
        }

        if (Category is not null)
        {
            return QueryShapeInputs.Units(source, Shape());
        }

        return [.. UnitPaths.Select(path => new ConversionSource
        {
            Name = path[(path.LastIndexOf('/') + 1)..],
            ContentType = SharedInputs.ContentTypeOf(path),
            Content = DifferentialData.Read($"inputs/{path}"),
        })];
    }

    private QueryShape Shape()
        => QueryShapeInputs.Categories.Single(shape => shape.Name == Category);
}

/// <summary>One pair of the criterion of F13: a source variant of a query against one translation of it.</summary>
internal sealed record DifferentialPair(DifferentialQuery Query, ORMEnum Source, ORMEnum Target);

/// <summary>A direction the matrix states as refused (decision 053).</summary>
internal sealed record RefusedDirection(DifferentialQuery Query, ORMEnum Source, ORMEnum Target, QueryFeature Feature);

/// <summary>A pair whose target writes the query in native SQL, with the feature its language does not speak (decision 113).</summary>
internal sealed record FallbackDirection(DifferentialQuery Query, ORMEnum Source, ORMEnum Target, QueryFeature Feature);

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

    /// <summary>Every (query, source, target) the matrix states and no target refuses - the pairs of F13.</summary>
    public static IEnumerable<DifferentialPair> Pairs()
        => Queries.SelectMany(query => query.Sources.SelectMany(source =>
            query.Targets(source).Select(target => new DifferentialPair(query, source, target))));

    /// <summary>
    /// Every direction the matrix states as refused. Decision 089 wants a refused direction
    /// said in that word rather than left out, and a suite asserts that the refusal really
    /// happens, so a target that starts accepting the query is noticed.
    /// </summary>
    public static IEnumerable<RefusedDirection> RefusedDirections()
        => Queries.SelectMany(query => query.Sources.SelectMany(source =>
            query.RefusedBy
                .Where(refusal => refusal.Key != source)
                .Select(refusal => new RefusedDirection(query, source, refusal.Key, refusal.Value))));

    /// <summary>
    /// Every pair whose target falls back to native SQL (decision 113): pairs of the matrix
    /// all the same, measured at the fourth level, and listed so that a suite asserts the
    /// record of the fallback - a target that began to speak the query, or one that began to
    /// fall back where it did not, shows up rather than passing as the other value of a cell.
    /// </summary>
    public static IEnumerable<FallbackDirection> FallbackDirections()
        => Queries.SelectMany(query => query.Sources.SelectMany(source =>
            query.FallbackBy
                .Where(fallback => fallback.Key != source)
                .Select(fallback => new FallbackDirection(query, source, fallback.Key, fallback.Value))));

    private static IReadOnlyList<DifferentialQuery> Parse()
        => [.. SharedInputs.Sections("matrix.txt", DifferentialData.ReadLines("matrix.txt"))
            .Select(section => Build(section.Id, section.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)))];

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

        // A query is either the matrix's own - one source and its units - or a category of
        // the manifest, whose sources and units the manifest states. Stating both would be
        // two answers to one question, so it is refused.
        string? category = values.GetValueOrDefault("category");
        IReadOnlyList<ORMEnum> sources;
        IReadOnlyList<string> unitPaths;
        IReadOnlyDictionary<ORMEnum, QueryFeature> refusedBy;
        IReadOnlyDictionary<ORMEnum, QueryFeature> fallbackBy;

        if (category is not null)
        {
            if (values.ContainsKey("source") || values.ContainsKey("units"))
            {
                throw new InvalidOperationException(
                    $"matrix.txt: [{id}] names the category {category} and states a source or units of its own; the manifest states those.");
            }

            var manifest = QueryShapeInputs.Categories.SingleOrDefault(s => s.Name == category)
                ?? throw new InvalidOperationException($"matrix.txt: [{id}] names the category {category}, which categories.txt does not state.");

            // Every source the manifest lists, the ones it lists under refusedWithoutCatalog
            // included: that refusal is of a Dapper parameter with nothing to type it from
            // (decision 083), and this matrix runs with the catalog of the fixture, which the
            // query asks for the binding itself, whichever the target (decision 105) - so the
            // source's own run, the identity direction, runs, and the source is a source
            // (decision 089).
            sources = [.. manifest.Sources.Keys];
            unitPaths = [];
            refusedBy = manifest.RefusedBy;
            fallbackBy = manifest.FallbackBy;
        }
        else
        {
            sources = [Enum.Parse<ORMEnum>(Required("source"))];
            unitPaths = SharedInputs.List(Required("units"));
            refusedBy = new Dictionary<ORMEnum, QueryFeature>();
            fallbackBy = new Dictionary<ORMEnum, QueryFeature>();
        }

        return new DifferentialQuery(
            id,
            category,
            sources,
            unitPaths,
            SharedInputs.List(Required("fields")),
            shape == "projection",
            bool.Parse(Required("ordered")),
            new ResultRow.RenderSettings(
                Number("decimalScale", 6),
                Number("floatDigits", 12),
                Number("fractionalSeconds", 3)),
            values.TryGetValue("arguments", out var arguments) ? Arguments(arguments) : [],
            SharedInputs.List(Required("mutations")),
            refusedBy,
            fallbackBy);
    }

    private static List<DifferentialArgument> Arguments(string value)
        => [.. SharedInputs.List(value).Select(entry =>
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
