using System.Text;
using System.Text.RegularExpressions;
using Model;
using Tests.Combined;
using Tests.Differential;

namespace Tests.Foreign;

/// <summary>
/// The examples of the variant <c>few-shot</c> (decisions 175 and 189): the hand-written artifacts
/// under <c>Tests/Database/Foreign</c> whose directory starts with <c>few-shot-</c>, one per chosen
/// category of the matrix and target. Each is the whole translation of the category's input - the
/// domain of the query-shape matrices and the one query - in the form the target gives it, and the
/// path judges every one at the fourth level like the other hand-written artifacts
/// (<c>expected.txt</c>). The categories are the four shapes a query is made of - a projection, a
/// filter, a join, a grouping -, every source states them, and they are four so that three remain
/// when one of them is the measured key.
///
/// A prompt of a cell gets three of them in its direction: the input of the example as the suite
/// exports it for the cell's source, and the artifact as the answer, in the form the reply takes.
/// The example whose key the cell measures is left out. The domain is translated once, before the
/// queries, because every example of a target carries the same domain - and not at all when the
/// input of the cell names a class of it: the matrix and the series are over that domain, and a
/// prompt never carries the entities of the measured input in the target's form (decision 189).
/// </summary>
internal static class WebExamples
{
    public const string Prefix = "few-shot-";

    /// <summary>How many examples a prompt takes (decision 175).</summary>
    public const int PerPrompt = 3;

    /// <summary>The slot of a template the examples fill; a template without it is one of another variant.</summary>
    public const string Slot = "{{EXAMPLES}}";

    private static readonly Regex ClassName = new(@"\bclass\s+([A-Za-z_]\w*)", RegexOptions.CultureInvariant);

    private static readonly Lazy<IReadOnlyList<ForeignArtifact>> Read = new(() =>
    {
        var root = ForeignArtifactPathTest.OwnArtifactsDirectory;
        var artifacts = Directory.GetDirectories(root)
            .Where(directory => Path.GetFileName(directory).StartsWith(Prefix, StringComparison.Ordinal) && File.Exists(Path.Combine(directory, ForeignArtifact.ManifestFileName)))
            .Order(StringComparer.Ordinal)
            .Select(directory => ForeignArtifact.Read(directory, ForeignInputs.SchemaPlaceholder))
            .ToList();

        return artifacts.Count > 0
            ? artifacts
            : throw new InvalidOperationException($"No example of the variant few-shot: no directory {Prefix}* with an {ForeignArtifact.ManifestFileName} under {root} (decision 189).");
    });

    /// <summary>Every example artifact, the schema placeholder kept as the inputs keep it.</summary>
    public static IReadOnlyList<ForeignArtifact> Artifacts => Read.Value;

    /// <summary>The queries of the matrix the examples translate, in the order of the matrix.</summary>
    public static IReadOnlyList<DifferentialQuery> Queries
        => [.. DifferentialMatrix.Queries.Where(query => query.Category is not null && Artifacts.Any(artifact => artifact.Key.Kind == ForeignKey.Matrix && artifact.Key.Id == query.Id))];

    /// <summary>One example: the query of the matrix, whose input the source states, and the artifact of the target.</summary>
    internal sealed record Example(DifferentialQuery Query, ForeignArtifact Artifact);

    /// <summary>The examples of a direction - the source states the query, the target has the artifact -, in the order of the matrix, the measured key left out.</summary>
    public static List<Example> Of(ORMEnum source, ORMEnum target, ForeignKey? measured)
        => [.. Queries
            .Where(query => query.Sources.Contains(source) && !(measured is { Kind: ForeignKey.Matrix } && string.Equals(measured.Id, query.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(query => (Query: query, Artifact: Artifacts.FirstOrDefault(artifact => artifact.Target == target && artifact.Key.Id == query.Id)))
            .Where(pair => pair.Artifact is not null)
            .Select(pair => new Example(pair.Query, pair.Artifact!))];

    /// <summary>Whether an input - the texts of its units - names a class of the examples' domain.</summary>
    public static bool SharesTheDomain(ForeignArtifact example, IEnumerable<string> inputContents)
    {
        var names = DomainUnits(example)
            .SelectMany(unit => ClassName.Matches(unit.Content).Select(match => match.Groups[1].Value))
            .Distinct()
            .Select(name => new Regex($@"\b{Regex.Escape(name)}\b", RegexOptions.CultureInvariant))
            .ToList();

        return inputContents.Any(content => names.Any(name => name.IsMatch(content)));
    }

    /// <summary>The units of the domain: the entity classes and the mapping documents of the entities. In MyBatis every document is a query's (decision 084), so none is.</summary>
    public static IEnumerable<ForeignUnit> DomainUnits(ForeignArtifact artifact)
        => artifact.Units.Where(unit => !IsQueryUnit(artifact.Target, unit));

    /// <summary>The units of the query: the binding method and the texts beside it, in MyBatis the mapper document too.</summary>
    public static IEnumerable<ForeignUnit> QueryUnits(ForeignArtifact artifact)
        => artifact.Units.Where(unit => IsQueryUnit(artifact.Target, unit));

    private static bool IsQueryUnit(ORMEnum target, ForeignUnit unit)
        => unit.ContentType.IsQuery() || (target == ORMEnum.MyBatis && unit.ContentType == ConversionContentType.XML);

    /// <summary>The section of a prompt (decision 189): the domain once where the input is over another one, then the three examples of the direction.</summary>
    public static string Render(ORMEnum source, ORMEnum target, ForeignKey measured, IReadOnlyList<string> inputContents)
    {
        var examples = Of(source, target, measured);
        if (examples.Count < PerPrompt)
        {
            throw new InvalidOperationException($"The direction {source} → {target} has {examples.Count} examples of the variant few-shot apart from {measured}, and a prompt takes {PerPrompt} (decision 175); the examples are the artifacts {Prefix}* under Tests/Database/Foreign.");
        }

        examples = [.. examples.Take(PerPrompt)];
        var shared = SharesTheDomain(examples[0].Artifact, inputContents);

        var text = new StringBuilder();
        text.Append("Three translations from ").Append(WebPrompt.DisplayName(source)).Append(" into ").Append(WebPrompt.DisplayName(target))
            .Append(", each in the form the answer takes - the files of the answer, without the report. ");
        text.Append(shared
            ? "Their queries run over the same entity classes as the input below, so the examples show the queries alone; the entity classes are not translated here.\n\n"
            : "They translate queries over another set of entity classes than the input's. Those entity classes are translated first, once; each example then translates one query over them.\n\n");

        // Every block of a file ends in a blank line, so a heading follows without one of its own.
        if (!shared)
        {
            text.Append("### The entity classes of the examples\n\nInput:\n\n");
            foreach (var unit in QueryShapeInputs.MappingUnits(source))
            {
                text.Append(WebPrompt.FileBlock(unit.Name!, null, ForeignInputs.Placeholder(unit.Content)));
            }

            text.Append("Answer:\n\n");
            foreach (var unit in DomainUnits(examples[0].Artifact))
            {
                text.Append(WebPrompt.FileBlock(unit.Path, unit.ContentType, unit.Content));
            }
        }

        for (var index = 0; index < examples.Count; index++)
        {
            var (query, artifact) = examples[index];
            text.Append("### Example ").Append(index + 1).Append("\n\nInput:\n\n");
            foreach (var unit in QueryShapeInputs.Categories.Single(shape => shape.Name == query.Category).Sources[source])
            {
                text.Append(WebPrompt.FileBlock(unit.Name!, null, ForeignInputs.Placeholder(unit.Content)));
            }

            text.Append("Answer:\n\n");
            foreach (var unit in QueryUnits(artifact))
            {
                text.Append(WebPrompt.FileBlock(unit.Path, unit.ContentType, unit.Content));
            }
        }

        return text.ToString().TrimEnd('\n');
    }
}
