using Model;
using SampleData;
using Tests.CatalogRuns;

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
/// stay out of git (decision 042), except the judge made from a documented run. The run is
/// <see cref="CatalogRun"/>, the same mechanism the SEDE catalog runs by (decision 208); what
/// holds of it without a database is <c>CatalogRuns/CatalogRunTest</c>.
/// </summary>
public class FibenRunTest(ITestOutputHelper output)
{
    public static TheoryData<string, ORMEnum> Cells()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in FibenSample.Queries)
        {
            foreach (var framework in CatalogRun.Frameworks)
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

        var query = run.Query(key);
        var cell = run.Cell(key, framework);

        if (!query.IsTranslated)
        {
            Assert.True(cell.Refused is not null, $"{key} is marked as not translated, but {framework} translated it: {cell.Describe()}");
            return;
        }

        Assert.True(cell.Refused is null, cell.Describe());
        Assert.True(cell.Unprepared is null, cell.Describe());
    }

    /// <summary>
    /// The fourth level between the frameworks (decision 089): the artifact of the target -
    /// Dapper's among them - returns the rows the catalog's own text returns over ADO.NET
    /// (decision 209), in the canonical form of decision 170: in the order of the query's
    /// ordering key, the rows of one key value as a set, the whole result as a set where the
    /// query does not order. A disagreement is a wrong translation, or a difference the
    /// frameworks make that the text does not.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryTargetReturnsTheRowsOfTheSource(string key, ORMEnum framework)
    {
        var run = FibenRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        if (!run.Query(key).IsTranslated)
        {
            Assert.Skip($"{key} is not translated.");
        }

        var source = run.Source(key);
        var cell = run.Cell(key, framework);

        Assert.True(source.Ran, source.Describe());
        Assert.True(cell.Ran, cell.Describe());
        Assert.True(run.AgreesWithSource(cell), $"{cell.Describe()} differs from the source: {cell.SourceDifference}");
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

        if (!run.Query(key).IsTranslated)
        {
            Assert.Skip($"{key} is not translated, so the judge holds no entry for it.");
        }

        var source = run.Source(key);
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
}
