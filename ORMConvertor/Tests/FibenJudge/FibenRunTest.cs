using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Model;
using SampleData;
using Tests.Combined;

namespace Tests.FibenJudge;

/// <summary>
/// The on-demand run of the FIBEN catalog (decision 149) in the .NET suite: the three .NET
/// frameworks over the Fiben database, in the order of the decision - the catalog is complete
/// (<c>Combined/FibenCatalogTest</c> holds that without a database), every query reaches every
/// target with the catalog and only the entities it reads, the artifacts compile, every target
/// returns the rows the source returns, the source returns the rows the judge holds, and every
/// cell is measured. The run is not a test of a build: without a configured Fiben database
/// every case skips with the reason, and fails where ORMCONVERTOR_REQUIRE_FIBEN_DATABASE
/// promised one. Its record goes into docs/runs beside the LDBC runs; the files it writes
/// stay out of git (decision 042), except the judge made from a documented run.
/// </summary>
public class FibenRunTest(ITestOutputHelper output)
{
    public static TheoryData<string, ORMEnum> Cells()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in FibenSample.Queries)
        {
            foreach (var framework in FibenRun.Frameworks)
            {
                data.Add(query.Key, framework);
            }
        }

        return data;
    }

    public static TheoryData<string> Keys() => [.. FibenSample.Queries.Select(query => query.Key)];

    /// <summary>
    /// The first and second level: a query translated as specified reaches the target without a
    /// Failure record, its artifact compiles and its factory accepts it; a query the catalog
    /// marks as not translated is refused. A target that writes the query in native SQL counts
    /// as reaching it - the third value of decision 113 - and the summary says how often.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryQueryReachesEveryDotNetTarget(string key, ORMEnum framework)
    {
        var run = FibenRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        var query = FibenSample.Queries.Single(candidate => candidate.Key == key);
        var cell = run.Cell(key, framework);

        if (query.Translation == FibenTranslation.NotTranslated)
        {
            Assert.True(cell.Refused is not null, $"{key} is marked as not translated, but {framework} translated it: {cell.Describe()}");
            return;
        }

        Assert.True(cell.Refused is null, cell.Describe());
        Assert.True(cell.Unprepared is null, cell.Describe());
    }

    /// <summary>
    /// The fourth level between the frameworks (decision 089): the artifact of the target
    /// returns the rows the catalog's own text returns through Dapper, in the canonical form
    /// of decision 170: in the order of the query's ordering key, the rows of one key value as
    /// a set, the whole result as a set where the query does not order. A disagreement
    /// is a wrong translation, or a difference the frameworks make that the text does not.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryTargetReturnsTheRowsOfTheSource(string key, ORMEnum framework)
    {
        var run = FibenRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        var query = FibenSample.Queries.Single(candidate => candidate.Key == key);
        if (query.Translation == FibenTranslation.NotTranslated)
        {
            Assert.Skip($"{key} is not translated.");
        }

        var source = run.Cell(key, FibenRun.Frameworks[0]);
        var cell = run.Cell(key, framework);

        Assert.True(source.Ran, source.Describe());
        Assert.True(cell.Ran, cell.Describe());
        Assert.True(run.AgreesWithSource(cell), $"{cell.Describe()} differs from the source: {FibenRun.Difference(source.Rows!, cell.Rows!)}");
    }

    /// <summary>
    /// The judge: the catalog's own text returns what the version of the judge's run returned.
    /// A disagreement is a change of the text or of the data that a new judge has to document,
    /// never something to be fixed in the comparison. Until a judge exists the case skips and
    /// says that this run can make one. A query the catalog marks as not translated has no
    /// entry, because the run the judge was made from refused it, and skips with that reason.
    /// </summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void TheSourceReturnsTheRowsOfTheJudge(string key)
    {
        var run = FibenRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        if (!run.HasJudge)
        {
            Assert.Skip("The Fiben database holds no judge yet; the source rows of this run (judge-candidate.tsv in the results directory) become one once the run is documented (decision 149).");
        }

        if (FibenSample.Queries.Single(candidate => candidate.Key == key).Translation == FibenTranslation.NotTranslated)
        {
            Assert.Skip($"{key} is not translated, so the judge holds no entry for it.");
        }

        var source = run.Cell(key, FibenRun.Frameworks[0]);
        Assert.True(source.Ran, source.Describe());
        Assert.True(run.AgreesWithJudge(key) == true, $"{key}: the source differs from the judge: {run.JudgeDifference(key)}");
    }

    /// <summary>The record of the run, printed whole: the summary, per framework and per query.</summary>
    [Fact]
    public void TheRunIsRecorded()
    {
        var run = FibenRun.Current;
        run.SkipIfUnavailable();

        Assert.Equal(FibenSample.DataSet, run.DataSet);
        foreach (var line in run.Describe())
        {
            output.WriteLine(line);
        }
    }

    [Fact]
    public void AMissingDatabaseSkipsUnlessTheEnvironmentPromisedIt()
    {
        Assert.Null(FibenDatabase.FailureOf("no Fiben", required: false));

        var failure = FibenDatabase.FailureOf("no Fiben", required: true);
        Assert.NotNull(failure);
        Assert.Contains("ORMCONVERTOR_REQUIRE_FIBEN_DATABASE", failure);
        Assert.Contains("no Fiben", failure);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("q1, q2 ,q237", "q1,q2,q237")]
    public void TheLimitIsAListOfKeys(string? configured, string? keys)
        => Assert.Equal(keys, FibenDatabase.ResolveKeys(configured) is { } set ? string.Join(",", set.Order(StringComparer.Ordinal)) : null);

    /// <summary>The connection handed to a cell carries the limit of one execution (decision 146), whatever the configured string said.</summary>
    [Theory]
    [InlineData("Server=db,1433;Database=Fiben;User ID=sa;Password=p;TrustServerCertificate=true;")]
    [InlineData("Server=db,1433;Database=Fiben;Command Timeout=30;Trusted_Connection=True;")]
    public void TheConnectionHandedToACellCarriesTheTimeLimit(string configured)
    {
        var builder = new SqlConnectionStringBuilder(FibenDatabase.WithCommandTimeout(configured));

        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds, builder.CommandTimeout);
        Assert.Equal("Fiben", builder.InitialCatalog);
    }

    /// <summary>
    /// The run measures only over a server in the measurement mode (decisions 164 and 166):
    /// single-threaded plans on the two processors of its own. A parallel server, the judge's
    /// mode with every processor of the VM, and a server that does not say how many it has leave
    /// the measurement out by themselves, and the environment can leave it out over any server.
    /// </summary>
    [Theory]
    [InlineData(true, 1, 2, null)]
    [InlineData(true, 0, 2, "MAXDOP 0")]
    [InlineData(true, 4, 4, "MAXDOP 4")]
    [InlineData(true, 1, 4, "4 processors")]
    [InlineData(true, 1, null, "unknown number")]
    [InlineData(false, 1, 2, "left out")]
    public void TheRunMeasuresOnlyOverAServerInTheMeasurementMode(bool configured, int maxdop, int? processors, string? reason)
    {
        var leftOut = FibenServer.MeasurementLeftOut(configured, maxdop, processors);

        if (reason is null)
        {
            Assert.Null(leftOut);
        }
        else
        {
            Assert.Contains(reason, leftOut, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The Java run measures with the same policy (decision 145) and the same limit (decision
    /// 146): its constants are read from its source, as the LDBC judge's limit is.
    /// </summary>
    [Fact]
    public void TheJavaRunMeasuresByTheSamePolicy()
    {
        var source = File.ReadAllText(Path.Combine(
            TargetFrameworkDescriptorTest.SolutionDirectory(),
            "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "fiben", "FibenRun.java"));

        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.WarmupIterations, Declared(source, "WARMUP_ITERATIONS"));
        Assert.Equal(FibenServer.MeasurementProcessors, Declared(source, "MEASUREMENT_PROCESSORS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.MinIterations, Declared(source, "MIN_ITERATIONS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.TargetMilliseconds, Declared(source, "TARGET_MILLISECONDS"));
        Assert.Equal(AdvisorBenchmarking.BenchmarkPolicy.ExecutionTimeoutSeconds, Declared(source, "QUERY_TIMEOUT_SECONDS"));
    }

    /// <summary>
    /// The processors the run expects of a server in the measurement mode are the ones the
    /// compose file gives both databases by default (decisions 145 and 166).
    /// </summary>
    [Fact]
    public void TheMeasurementProcessorsAreTheDefaultCpusetOfTheDatabases()
    {
        var compose = File.ReadAllText(Path.Combine(TargetFrameworkDescriptorTest.SolutionDirectory(), "docker-compose.yml"));
        var defaults = Regex.Matches(compose, @"cpuset: ""\$\{DATABASE_CPUSET-([0-9,\-]+)\}""").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(2, defaults.Count);
        Assert.All(defaults, cpuset => Assert.Equal(FibenServer.MeasurementProcessors, Processors(cpuset)));
    }

    private static int Processors(string cpuset)
        => cpuset.Split(',').Sum(part => part.Split('-') is [var first, var last] ? int.Parse(last, CultureInfo.InvariantCulture) - int.Parse(first, CultureInfo.InvariantCulture) + 1 : 1);

    private static double Declared(string source, string constant)
    {
        var match = Regex.Match(source, constant + @"\s*=\s*([0-9.]+)\s*;");
        Assert.True(match.Success, $"The Java run declares {constant} in FibenRun.java.");
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
