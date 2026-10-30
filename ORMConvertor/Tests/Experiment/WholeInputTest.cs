using System.Diagnostics;
using System.Globalization;
using System.Text;
using Model;
using Tests.Database;

namespace Tests.Experiment;

/// <summary>
/// The run over the whole input (<see cref="WholeInputRun"/>): a run on demand, never a test of
/// a build. With <c>ORMCONVERTOR_EXPERIMENT_RESULTS</c> set, every input of the experiment run
/// (exported into <c>inputs/</c> where not yet there) is translated into each of the six
/// frameworks and the artifacts merged into one project per framework under
/// <c>whole-input/inputs/&lt;framework&gt;/</c>; then every one of the six is translated into every
/// framework, the six into themselves included, with the time, the catalog's share of it and the
/// memory in a row of <c>whole-input/whole-input-rows.tsv</c>, the Failure and Loss reasons by
/// count in <c>whole-input-reasons.txt</c> and the tables in <c>whole-input-summary.md</c>.
/// Resumable: a whole input already on disk is read, a row already written is not translated
/// again. Without the variable the case skips with the reason; a Failure record is a count, and
/// what fails is the run itself - a database it needs that is not there.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class WholeInputTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void TheWholeInputIsMergedAndTranslatedEveryWay()
    {
        var directory = WholeInputRun.Directory;
        if (directory is null)
        {
            Assert.Skip($"No experiment run configured: set {ExperimentRun.ResultsVariable} to the directory of the run (decision 173); the whole input goes under it.");
        }

        fixture.SkipIfUnavailable();
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(directory);

        var inputs = ExperimentRunTest.Inputs();
        output.WriteLine($"{inputs.Count} inputs of the kinds {string.Join(", ", ExperimentRun.Kinds.Order(StringComparer.Ordinal))}; case studies {(fixture.IsCaseStudyAvailable ? "available" : "not available: " + fixture.CaseStudySkipReason)}");

        var log = new StringBuilder();
        void Log(string line)
        {
            output.WriteLine(line);
            log.Append(line).Append('\n');
        }

        // 1. The six whole inputs, each from every input translated into its framework.
        var wholeInputs = new List<WholeInput>();
        foreach (var framework in Enum.GetValues<ORMEnum>())
        {
            var inputDirectory = WholeInputRun.InputDirectory(directory, framework);
            var existing = WholeInput.Read(inputDirectory);
            if (existing is not null)
            {
                Log($"whole input {framework}: read from disk, {existing.Units.Count} units ({existing.Entities} entities, {existing.Queries} queries, {existing.Bytes / 1024} KB)");
                wholeInputs.Add(existing);
                continue;
            }

            var clock = Stopwatch.StartNew();
            var whole = WholeInput.Merge(framework, inputDirectory, inputs, fixture, Log);
            Log($"whole input {framework}: {whole.FromInputs} inputs merged, {whole.Refused} refused, {whole.Threw} threw, {whole.Unavailable} left out; {whole.Units.Count} units ({whole.Entities} entities, {whole.Queries} queries, {whole.Bytes / 1024} KB); {whole.DuplicateEntityArtifacts} duplicate entity artifacts dropped, {whole.ConflictingEntityArtifacts} of them with another text; translations {whole.MergeTranslateMs / 1000:F1} s, of it catalog {whole.MergeCatalogMs / 1000:F1} s, wall {clock.Elapsed.TotalSeconds:F1} s");
            wholeInputs.Add(whole);
        }

        File.AppendAllText(Path.Combine(directory, "whole-input-merge.log"), log.ToString(), new UTF8Encoding(false));

        // 2. Every whole input into every framework.
        var rowsPath = Path.Combine(directory, WholeInputRun.RowsFileName);
        var reasonsPath = Path.Combine(directory, WholeInputRun.ReasonsFileName);
        var rows = WholeInputRow.ReadAll(rowsPath).ToDictionary(row => row.Id, StringComparer.Ordinal);

        using var catalog = WholeInputRun.Catalog(fixture, out var catalogDescription);
        output.WriteLine($"catalog of the run: {catalogDescription}");

        foreach (var source in wholeInputs.OrderBy(input => input.Framework))
        {
            foreach (var target in Enum.GetValues<ORMEnum>())
            {
                if (rows.ContainsKey($"{source.Framework}-{target}"))
                {
                    output.WriteLine($"{source.Framework} -> {target}: row already written");
                    continue;
                }

                var (row, conversion) = WholeInputRun.Translate(source, target, catalog);
                output.WriteLine(row.Describe());

                rows[row.Id] = row;
                WholeInputRow.WriteAll(rowsPath, rows.Values);

                var reasons = new StringBuilder();
                reasons.Append(row.Describe()).Append('\n');
                if (conversion is not null)
                {
                    foreach (var line in WholeInputRun.Reasons(conversion))
                    {
                        reasons.Append(line).Append('\n');
                    }
                }

                reasons.Append('\n');
                File.AppendAllText(reasonsPath, reasons.ToString(), new UTF8Encoding(false));
            }
        }

        // 3. The summary.
        var summary = WholeInputRun.Summary(directory, wholeInputs, [.. rows.Values], catalogDescription, Tree()).ToList();
        File.WriteAllText(Path.Combine(directory, WholeInputRun.SummaryFileName), string.Join('\n', summary) + "\n", new UTF8Encoding(false));
        foreach (var line in summary)
        {
            output.WriteLine(line);
        }

        Assert.Equal(wholeInputs.Count * Enum.GetValues<ORMEnum>().Length, rows.Count);
    }

    /// <summary>The commit the run stands on, as the build carries it, or the tool version where it carries none.</summary>
    private static string Tree()
        => typeof(OrmConvertor.ConversionHandler).Assembly
               .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
               .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
               .FirstOrDefault()?.InformationalVersion
           ?? typeof(OrmConvertor.ConversionHandler).Assembly.GetName().Version?.ToString()
           ?? "unknown";
}
