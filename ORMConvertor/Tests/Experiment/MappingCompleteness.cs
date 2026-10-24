using System.Globalization;
using System.Text;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using OrmConvertor.Factories;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// What the measure of the completeness of transferred mapping facts found for one artifact
/// (decision 173, the last metric of T3): how many facts the reference holds that the target
/// can express, how many of them the tool's ledger says the artifact carries, how many the
/// parser of the target reads back out of the artifact with the same value, and how many facts
/// the artifact states that the reference does not hold. One line of <c>mapping-rows.tsv</c>.
/// </summary>
internal sealed record MappingMeasure(
    string Artifact,
    string Answers,
    string Source,
    string Target,
    string Producer,
    int Expressible,
    int Carried,
    int ReadBack,
    int Invented,
    string Detail,
    string Metadata)
{
    public const string Header = "artifact\tanswers\tsource\ttarget\tproducer\texpressible\tcarried\treadBack\tinvented\tdetail\tmetadata";

    /// <summary>The share of expressible facts the artifact states, or null where the target expresses none of the reference's facts.</summary>
    public double? Share => Expressible == 0 ? null : (double)ReadBack / Expressible;

    public string Row()
        => string.Join('\t',
            Artifact, Answers, Source, Target, Producer,
            Expressible.ToString(CultureInfo.InvariantCulture),
            Carried.ToString(CultureInfo.InvariantCulture),
            ReadBack.ToString(CultureInfo.InvariantCulture),
            Invented.ToString(CultureInfo.InvariantCulture),
            Clean(Detail),
            Clean(Metadata));

    public string Describe()
        => $"{Artifact} ({Answers}, {Source} -> {Target}, {Producer}): {ReadBack} of {Expressible} expressible facts read back ({Carried} carried by the ledger), {Invented} invented"
           + (Detail.Length > 0 ? ": " + Detail : string.Empty);

    private static string Clean(string text)
        => text.Replace('\t', ' ').Replace("\r", string.Empty).Replace('\n', ' ');

    /// <summary>The file of mapping rows: one line per artifact under the header, rewritten whole on every change.</summary>
    public static void Write(string directory, string fileName, IEnumerable<MappingMeasure> measures)
    {
        Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(Header).Append('\n');
        foreach (var measure in measures.OrderBy(measure => measure.Artifact, StringComparer.Ordinal))
        {
            text.Append(measure.Row()).Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, fileName), text.ToString(), new UTF8Encoding(false));
    }
}

/// <summary>
/// The completeness of transferred mapping facts (decision 173): the artifact's entity and
/// mapping units are read back into the intermediate representation by the parser of the
/// target framework - the parsers are C# for every framework (decision 076), so the .NET suite
/// measures every target -, and the facts the representation then holds are compared with the
/// reference: the ledger of the tool's conversion of the same input into the same target
/// (decision 124), which lists every fact the representation held at generation time, with its
/// value. A fact counts as transferred when the read-back holds the same subject and category
/// with the same value; the denominator is the reference facts whose category the target's
/// descriptor can express, so that a target that cannot state a fact is not charged with it
/// (decision 009). The ledger's own verdict - carried or dropped - is written beside, so that
/// what the tool claims and what the artifact states are two columns and not one.
///
/// For a foreign artifact the reference is the same: the tool's conversion of the input the
/// artifact answers, because the facts the tool had are the facts a translation of that input
/// could transfer, whoever wrote it.
/// </summary>
internal static class MappingCompleteness
{
    public static MappingMeasure Measure(ForeignArtifact artifact, ConversionResult reference)
    {
        var descriptor = DescriptorFactory.Create(artifact.Target);

        var expressible = reference.Facts
            .Where(fact => fact.Outcome != FactOutcome.NotGenerated && descriptor.SupportOf(fact.Category) != FactSupport.NotExpressible)
            .ToList();

        var carried = expressible.Count(fact => fact.Outcome == FactOutcome.Carried);

        Dictionary<FactSubject, string?> readBack;
        try
        {
            readBack = ReadBack(artifact);
        }
        catch (Exception exception)
        {
            return new MappingMeasure(
                artifact.Id, artifact.Key.ToString(), artifact.Source.ToString(), artifact.Target.ToString(), artifact.Producer,
                expressible.Count, carried, 0, 0,
                "the parser of the target threw: " + exception.GetBaseException().Message, artifact.Metadata);
        }

        var missing = new List<string>();
        var transferred = 0;
        foreach (var fact in expressible)
        {
            var subject = new FactSubject(fact.Entity, fact.Property, fact.Category);
            if (readBack.TryGetValue(subject, out var value) && SameValue(fact.Category, fact.Value, value))
            {
                transferred++;
            }
            else
            {
                missing.Add($"{subject}: expected {Quote(fact.Value)}, read {(readBack.ContainsKey(subject) ? Quote(value) : "nothing")}{(fact.Outcome == FactOutcome.Dropped ? " (dropped by the ledger)" : string.Empty)}");
            }
        }

        var known = expressible.Select(fact => new FactSubject(fact.Entity, fact.Property, fact.Category)).ToHashSet();
        var invented = readBack.Keys.Where(subject => !known.Contains(subject)).OrderBy(subject => subject.ToString(), StringComparer.Ordinal).ToList();

        var detail = string.Join("; ", missing.Concat(invented.Select(subject => $"{subject}: stated by the artifact, {Quote(readBack[subject])}, not by the reference")));

        return new MappingMeasure(
            artifact.Id, artifact.Key.ToString(), artifact.Source.ToString(), artifact.Target.ToString(), artifact.Producer,
            expressible.Count, carried, transferred, invented.Count, detail, artifact.Metadata);
    }

    /// <summary>
    /// The facts the artifact states, read by the entity parsers of its target over its entity
    /// and mapping units - each under the language its role names, because an input unit declares
    /// a language and an artifact a role (decision 111) - in the vocabulary of the ledger.
    /// </summary>
    internal static Dictionary<FactSubject, string?> ReadBack(ForeignArtifact artifact)
    {
        var builder = EntityBuilderFactory.Create(artifact.Target)
            ?? throw new InvalidOperationException($"{artifact.Target} has no entity builder.");

        var parsers = ParserFactory.Create(artifact.Target, builder, qb: null).OfType<IEntityParser>().ToList();

        foreach (var parser in parsers)
        {
            foreach (var unit in artifact.Units.Where(unit => parser.CanParse(unit.ContentType.LanguageOf())))
            {
                parser.Parse(unit.Content);
            }
        }

        var facts = new Dictionary<FactSubject, string?>();
        foreach (var entityMap in builder.EntityMaps)
        {
            foreach (var (property, category, value) in AbstractEntityBuilder.MappingFactsOf(entityMap))
            {
                facts[new FactSubject(entityMap.Entity.Name, property, category)] = value;
            }
        }

        return facts;
    }

    /// <summary>
    /// Whether the value read back states the fact the reference states. Values are compared as
    /// the ledger spells them, with one allowance: a database type is compared by its family
    /// and facets only where the reference spells a literal type of the source's dialect that
    /// the target writes in its own vocabulary - the reading of the target's spelling is the
    /// target's statement of the same type (decision 019), and the literal spelling is kept
    /// beside it where the target carries it (decision 086).
    /// </summary>
    internal static bool SameValue(MappingFactCategory category, string? expected, string? actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            return true;
        }

        if (expected is null || actual is null)
        {
            return false;
        }

        return category switch
        {
            MappingFactCategory.DatabaseType => string.Equals(Normalize(expected), Normalize(actual), StringComparison.OrdinalIgnoreCase),
            MappingFactCategory.ForeignKeyColumns or MappingFactCategory.UniqueConstraint => SameSet(expected, actual),
            _ => false,
        };
    }

    /// <summary>A type spelling without its length or precision and without whitespace, so that <c>nvarchar(50)</c> and <c>NVARCHAR</c> name the same family.</summary>
    private static string Normalize(string type)
    {
        var bare = type.IndexOf('(') is var open and >= 0 ? type[..open] : type;
        return bare.Trim();
    }

    private static bool SameSet(string expected, string actual)
        => expected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(actual.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private static string Quote(string? value) => value is null ? "(stated)" : $"'{value}'";
}

/// <summary>The subject of one mapping fact: the entity by its simple name, the property or null for the entity itself, and the category.</summary>
internal readonly record struct FactSubject(string Entity, string? Property, MappingFactCategory Category)
{
    public override string ToString() => Property is null ? $"{Entity} {Category}" : $"{Entity}.{Property} {Category}";
}
