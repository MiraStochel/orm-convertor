using System.Text.Json;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using OrmConvertor;
using ORMConvertorAPI.Data;
using Tests.Database;
using Tests.FibenJudge;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// One input of the experiment run (decision 173): a directory the inputs export wrote
/// (<see cref="ForeignInputs"/>), read back - the key it answers, the source framework and the
/// units, with the schema of this suite substituted.
/// </summary>
internal sealed record ExperimentInput(string Directory, ForeignKey Key, ORMEnum Source, List<ConversionSource> Units)
{
    /// <summary>The name of the input's directory, which identifies the input in every row.</summary>
    public string Id => Path.GetFileName(Path.TrimEndingDirectorySeparator(Directory));

    /// <summary>The identifier of the row of one translation of the input.</summary>
    public string RowId(ORMEnum target) => $"{Id}-{target}";

    /// <summary>
    /// Every input under the directory: every directory directly under it that holds an
    /// <c>input.json</c>, in the order of their names. A root without any is a misconfiguration.
    /// </summary>
    public static IReadOnlyList<ExperimentInput> Discover(string root, string schema)
    {
        if (!System.IO.Directory.Exists(root))
        {
            throw new InvalidOperationException($"{ExperimentRun.InputsVariable} names {root}, which is not a directory; the inputs export of the suite writes one (decision 172).");
        }

        var inputs = System.IO.Directory.GetDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, ForeignInputs.ManifestFileName)))
            .Order(StringComparer.Ordinal)
            .Select(directory =>
            {
                var (key, source, units) = ForeignInputs.Read(directory, schema);
                return new ExperimentInput(directory, key, source, units);
            })
            .ToList();

        if (inputs.Count == 0)
        {
            throw new InvalidOperationException($"{root} holds no input: no directory under it has an {ForeignInputs.ManifestFileName}.");
        }

        return inputs;
    }
}

/// <summary>
/// The experiment run of the .NET suite (decision 173): the tool translates every input the
/// inputs export knows into every target, and the artifact goes through the four levels of the
/// path of decision 172 as a foreign artifact would, with the same measure at the fourth level
/// and the same row, producer <c>tool</c> - so that a row of the tool and a row of a model over
/// the same key are read side by side. The .NET suite judges the three .NET targets; the Java
/// suite judges the three Java targets the same way against an instance (decision 078). Beside
/// the levels, this suite measures the completeness of the transferred mapping facts for every
/// target, the Java ones included, by reading the artifact back with the parser of its target
/// (<see cref="MappingCompleteness"/>): the parsers are C# in every framework (decision 076).
///
/// Nothing here asserts about the tool: a refusal is a row, a red level is a row. What throws
/// is the path - a key no measure answers, a database a key needs that is not configured.
/// </summary>
internal static class ExperimentRun
{
    /// <summary>The directory of inputs the run translates; the inputs export writes it, and <c>ORMCONVERTOR_EXPERIMENT_RESULTS/inputs</c> is the default where it stands.</summary>
    public const string InputsVariable = "ORMCONVERTOR_EXPERIMENT_INPUTS";

    /// <summary>The directory the rows, the mapping rows and the summary of a run are written into; without it the run skips.</summary>
    public const string ResultsVariable = "ORMCONVERTOR_EXPERIMENT_RESULTS";

    /// <summary>The kinds of keys a run takes, as a comma-separated list of <c>matrix</c>, <c>cross</c>, <c>example</c>, <c>ldbc</c>, <c>fiben</c>; every kind the environment can measure without it.</summary>
    public const string KindsVariable = "ORMCONVERTOR_EXPERIMENT_KINDS";

    public const string RowsFileName = "tool-rows.tsv";

    public const string JavaRowsFileName = "java-tool-rows.tsv";

    public const string MappingRowsFileName = "mapping-rows.tsv";

    public const string Producer = "tool";

    public static string? ResultsDirectory
        => Environment.GetEnvironmentVariable(ResultsVariable) is { } directory && !string.IsNullOrWhiteSpace(directory) ? directory.Trim() : null;

    public static string? InputsDirectory
        => Environment.GetEnvironmentVariable(InputsVariable) is { } directory && !string.IsNullOrWhiteSpace(directory)
            ? directory.Trim()
            : ResultsDirectory is { } results ? Path.Combine(results, "inputs") : null;

    /// <summary>The kinds of keys the environment lets this run measure: a FIBEN key needs the Fiben database, every other kind the fixture.</summary>
    public static IReadOnlySet<string> Kinds
    {
        get
        {
            var asked = Environment.GetEnvironmentVariable(KindsVariable);
            var kinds = string.IsNullOrWhiteSpace(asked)
                ? new HashSet<string>([ForeignKey.Matrix, ForeignKey.Cross, ForeignKey.Example, ForeignKey.Ldbc, ForeignKey.Fiben], StringComparer.Ordinal)
                : asked.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(kind => kind.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

            if (!FibenDatabase.IsConfigured)
            {
                kinds.Remove(ForeignKey.Fiben);
            }

            return kinds;
        }
    }

    /// <summary>
    /// The catalog a translation of the key reads (decision 015): the fixture for the matrix, the
    /// scenarios and the LDBC catalog over its empty tables; the case-study database for a case
    /// study, over the empty tables of its project's schema (decision 176); the Fiben database
    /// for FIBEN; none for an example written for the tool (decision 099).
    /// </summary>
    public static ICatalogReader? CatalogFor(ForeignKey key, TestSchemaFixture fixture) => key.Kind switch
    {
        ForeignKey.Fiben => FibenCatalog.Value,
        ForeignKey.Example when IsCaseStudy(key) => fixture.IsCaseStudyAvailable ? fixture.CaseStudyCatalogReader : null,
        ForeignKey.Example => null,
        _ => fixture.IsAvailable ? fixture.CatalogReader : null,
    };

    /// <summary>Whether the key is an excerpt of a real project - the case study of T1 (decision 174) -, whose tables stand in the case-study database (decision 176).</summary>
    public static bool IsCaseStudy(ForeignKey key) => key.Kind == ForeignKey.Example && Examples.RealProjectKeys.Contains(key.Id);

    /// <summary>Why a translation of the key cannot be made here, or null where its database stands: the fixture for every kind but FIBEN and the examples, the case-study database for a case study.</summary>
    public static string? Unavailable(ForeignKey key, TestSchemaFixture fixture) => key.Kind switch
    {
        ForeignKey.Fiben => null,
        ForeignKey.Example when IsCaseStudy(key) => fixture.IsCaseStudyAvailable ? null : fixture.CaseStudySkipReason,
        ForeignKey.Example => null,
        _ => fixture.IsAvailable ? null : fixture.SkipReason,
    };

    private static readonly Lazy<ICatalogReader?> FibenCatalog = new(() =>
        FibenDatabase.ConnectionString is { } connectionString ? new CachingCatalogReader(new SqlServerCatalogReader(connectionString)) : null);

    /// <summary>
    /// Translates the input into the target and judges the artifact. A translation with a
    /// Failure record is the row <c>refused</c> at level 0, whatever the tool handed over beside
    /// the refusal; otherwise the artifact is judged as a foreign artifact of producer
    /// <c>tool</c> is. The callback sees the verdict after every level.
    /// </summary>
    public static (ForeignVerdict Verdict, ConversionResult? Conversion) Judge(ExperimentInput input, ORMEnum target, TestSchemaFixture fixture, Action<ForeignVerdict>? progress = null)
    {
        var (conversion, artifact, refusal) = Translate(input, target, fixture);

        if (artifact is null)
        {
            var refused = new ForeignVerdict
            {
                Artifact = input.RowId(target),
                Answers = input.Key.ToString(),
                Source = input.Source.ToString(),
                Target = target.ToString(),
                Producer = Producer,
                Language = ForeignArtifact.LanguageRefused,
                Metadata = Metadata(conversion),
                Outcome = ForeignVerdict.Refused,
                Detail = refusal!,
            };

            progress?.Invoke(refused);
            return (refused, conversion);
        }

        return (ForeignArtifactRun.Run(artifact, progress), conversion);
    }

    /// <summary>
    /// The tool's translation of the input as an artifact of the path: the conversion, the
    /// artifact built from its sources - or null with the reasons where a Failure record stands
    /// in the conversion (decision 173 reads any Failure as a refusal of the cell, as the FIBEN
    /// run does), or where the tool threw.
    /// </summary>
    public static (ConversionResult? Conversion, ForeignArtifact? Artifact, string? Refusal) Translate(ExperimentInput input, ORMEnum target, TestSchemaFixture fixture)
    {
        ConversionResult conversion;
        try
        {
            conversion = ConversionHandler.Convert(input.Source, target, input.Units, CatalogFor(input.Key, fixture));
        }
        catch (Exception exception)
        {
            return (null, null, "the conversion threw: " + exception.GetBaseException().Message);
        }

        var failures = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Failure).Select(record => record.Reason).Distinct().ToList();
        if (failures.Count > 0)
        {
            return (conversion, null, string.Join("; ", failures));
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var units = conversion.Sources
            .Select((artifact, index) => new ForeignUnit(
                Unique(artifact.Name ?? $"{artifact.ContentType}-{index + 1}{ForeignInputs.Extension(artifact.ContentType)}", taken),
                artifact.ContentType,
                artifact.Content))
            .ToList();

        var foreign = new ForeignArtifact(input.RowId(target), input.Directory, input.Key, input.Source, target, Producer, units, Metadata(conversion));
        return (conversion, foreign, null);
    }

    /// <summary>What the row of a tool artifact carries as metadata: the versions the run record of S6 names, and whether the query went out in native SQL.</summary>
    private static string Metadata(ConversionResult? conversion)
    {
        if (conversion is null)
        {
            return "{}";
        }

        var fallbacks = conversion.Records.Where(record => record.Kind == ConversionRecordKind.Fallback).Select(record => record.Feature?.ToString() ?? "-").Distinct().ToList();
        var metadata = new Dictionary<string, object?>
        {
            ["toolVersion"] = conversion.ToolVersion,
            ["sourceFrameworkVersion"] = conversion.SourceFrameworkVersion,
            ["targetFrameworkVersion"] = conversion.TargetFrameworkVersion,
            ["catalogState"] = conversion.CatalogState.ToString(),
        };

        if (fallbacks.Count > 0)
        {
            metadata["fallback"] = string.Join(", ", fallbacks);
        }

        return JsonSerializer.Serialize(metadata);
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        if (taken.Add(name))
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        for (var number = 2; ; number++)
        {
            var candidate = $"{stem}-{number}{extension}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
