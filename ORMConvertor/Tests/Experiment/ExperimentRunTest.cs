using Model;
using Tests.Database;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// The experiment run of the .NET suite (decision 173): a run on demand, never a test of a
/// build. With <c>ORMCONVERTOR_EXPERIMENT_RESULTS</c> set, the inputs the suite knows are written
/// into <c>inputs/</c> under it where they are not yet there (the export of decision 172), every
/// input is translated by the tool into every .NET target and judged through the levels of the
/// path, with a row per translation in <c>tool-rows.tsv</c>, rewritten after every level; and
/// every input is translated into every target of the six and its artifact read back by the
/// parser of the target, with a row per translation in <c>mapping-rows.tsv</c>. The Java suite
/// writes <c>java-tool-rows.tsv</c> over the same inputs, and <c>ExperimentSummaryTest</c> the
/// table over all of it. Without the variable every case skips with the reason; a refusal or a
/// red level is a row and not a failure - only the path itself fails, by name.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class ExperimentRunTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private const string None = "";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ForeignVerdict> Verdicts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MappingMeasure> Measures = new(StringComparer.Ordinal);

    /// <summary>The inputs of the run, exported where they are not yet on disk; one empty entry where no run is configured.</summary>
    internal static IReadOnlyList<ExperimentInput> Inputs()
    {
        var inputs = ExperimentRun.InputsDirectory;
        if (inputs is null)
        {
            return [];
        }

        lock (Gate)
        {
            if (!Directory.Exists(inputs) || Directory.GetDirectories(inputs).Length == 0)
            {
                ForeignInputs.Export(inputs);
            }
        }

        var kinds = ExperimentRun.Kinds;
        return ExperimentInput.Discover(inputs, TestDatabase.SchemaName).Where(input => kinds.Contains(input.Key.Kind)).ToList();
    }

    public static TheoryData<string, ORMEnum> Translations()
    {
        var data = new TheoryData<string, ORMEnum>();
        var inputs = Inputs();
        if (inputs.Count == 0)
        {
            data.Add(None, ORMEnum.Dapper);
            return data;
        }

        foreach (var input in inputs)
        {
            foreach (var target in Enum.GetValues<ORMEnum>().Where(ForeignArtifactRun.Owns))
            {
                data.Add(input.Directory, target);
            }
        }

        return data;
    }

    public static TheoryData<string, ORMEnum> Mappings()
    {
        var data = new TheoryData<string, ORMEnum>();
        var inputs = Inputs();
        if (inputs.Count == 0)
        {
            data.Add(None, ORMEnum.Dapper);
            return data;
        }

        foreach (var input in inputs)
        {
            foreach (var target in Enum.GetValues<ORMEnum>())
            {
                data.Add(input.Directory, target);
            }
        }

        return data;
    }

    /// <summary>The levels of the path over the tool's own translation, one row per translation into a .NET target.</summary>
    [Theory]
    [MemberData(nameof(Translations))]
    public void TheToolsArtifactIsJudgedAndRecorded(string directory, ORMEnum target)
    {
        var input = Input(directory);
        if (ExperimentRun.Unavailable(input.Key, fixture) is { } unavailable)
        {
            Assert.Fail($"{input.Id} needs a database as its catalog or to run over, and it is not available: {unavailable}");
        }

        var (verdict, _) = ExperimentRun.Judge(input, target, fixture, RecordVerdict);
        output.WriteLine(verdict.Describe());
    }

    /// <summary>The completeness of the transferred mapping facts over the tool's own translation, one row per translation into any of the six targets.</summary>
    [Theory]
    [MemberData(nameof(Mappings))]
    public void TheMappingOfTheToolsArtifactIsMeasured(string directory, ORMEnum target)
    {
        var input = Input(directory);
        var (conversion, artifact, refusal) = ExperimentRun.Translate(input, target, fixture);

        var measure = artifact is null || conversion is null
            ? new MappingMeasure(input.RowId(target), input.Key.ToString(), input.Source.ToString(), target.ToString(), ExperimentRun.Producer, 0, 0, 0, 0, "refused: " + refusal, "{}")
            : MappingCompleteness.Measure(artifact, conversion);

        output.WriteLine(measure.Describe());
        RecordMeasure(measure);
    }

    private static ExperimentInput Input(string directory)
    {
        if (directory.Length == 0)
        {
            Assert.Skip($"No experiment run configured: set {ExperimentRun.ResultsVariable} to the directory of the run (decision 173).");
        }

        var (key, source, units) = ForeignInputs.Read(directory, TestDatabase.SchemaName);
        return new ExperimentInput(directory, key, source, units);
    }

    private static void RecordVerdict(ForeignVerdict verdict)
    {
        lock (Gate)
        {
            Verdicts[verdict.Artifact] = verdict;
            if (ExperimentRun.ResultsDirectory is { } directory)
            {
                ForeignRows.Write(directory, ExperimentRun.RowsFileName, Verdicts.Values);
            }
        }
    }

    private static void RecordMeasure(MappingMeasure measure)
    {
        lock (Gate)
        {
            Measures[measure.Artifact] = measure;
            if (ExperimentRun.ResultsDirectory is { } directory)
            {
                MappingMeasure.Write(directory, ExperimentRun.MappingRowsFileName, Measures.Values);
            }
        }
    }
}

/// <summary>
/// The table of T2 × T3 over the rows of a run (decision 173): run last, after both suites wrote
/// their rows into the directory, because the Java rows are not there before the Java suite ran.
/// </summary>
public class ExperimentSummaryTest(ITestOutputHelper output)
{
    [Fact]
    public void TheSummaryIsWritten()
    {
        var directory = ExperimentRun.ResultsDirectory;
        if (directory is null)
        {
            Assert.Skip($"No experiment run configured: set {ExperimentRun.ResultsVariable} to the directory of the run (decision 173).");
        }

        if (!Directory.Exists(directory))
        {
            Assert.Fail($"{directory} does not exist; the summary is written over the rows of a run.");
        }

        foreach (var line in ExperimentSummary.Write(directory))
        {
            output.WriteLine(line);
        }

        Assert.True(File.Exists(Path.Combine(directory, ExperimentSummary.FileName)));
    }
}
