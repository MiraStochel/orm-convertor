using Model;
using SampleData;
using Tests.CatalogRuns;

namespace Tests.SedeJudge;

/// <summary>
/// The on-demand run of the SEDE catalog (decision 208) in the .NET suite: the three .NET
/// frameworks over the Sede database, in the stages of the FIBEN run - the catalog is complete
/// (<c>Combined/SedeCatalogTest</c> holds that without a database), every query reaches every
/// target with the catalog and only the entities it reads, the artifacts compile, every target
/// returns the rows the source returns with the catalog's values, the source returns the rows
/// the judge holds, and every cell is measured. The run is not a test of a build: without a
/// configured Sede database every case skips with the reason, and fails where
/// ORMCONVERTOR_REQUIRE_SEDE_DATABASE promised one. Its record goes into docs/runs; the files
/// it writes stay out of git (decision 042), except the judge made from a documented run. The
/// run is <see cref="CatalogRun"/>, the same mechanism the FIBEN catalog runs by; what holds of
/// it without a database is <c>CatalogRuns/CatalogRunTest</c>.
///
/// The catalog claims every valid read as translated and the run shows which target carries
/// it: a red cell here is the measure of a version over text the tool's authors never wrote,
/// not a defect of the catalog, and the record of the run names what it was.
/// </summary>
public class SedeRunTest(ITestOutputHelper output)
{
    public static TheoryData<string, ORMEnum> Cells()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in SedeSample.Queries)
        {
            foreach (var framework in CatalogRun.Frameworks)
            {
                data.Add(query.Key, framework);
            }
        }

        return data;
    }

    public static TheoryData<string> Keys() => [.. SedeSample.Queries.Select(query => query.Key)];

    /// <summary>
    /// The first and second level: a query translated as the author wrote it reaches the target
    /// without a Failure record, its artifact compiles and its factory accepts it. A query the
    /// catalog marks as not translated makes no claim - the tool may refuse it or translate a
    /// text that then fails over the schema, and the record says which -, so its cases skip.
    /// A target that writes the query in native SQL counts as reaching it - the third value of
    /// decision 113 - and the summary says how often.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryQueryReachesEveryDotNetTarget(string key, ORMEnum framework)
    {
        var run = SedeRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        var query = run.Query(key);
        var cell = run.Cell(key, framework);

        if (!query.IsTranslated)
        {
            Assert.Skip($"{key} is not translated: the catalog claims nothing of it, and the record says what {framework} did with it ({cell.Outcome}).");
        }

        Assert.True(cell.Refused is null, cell.Describe());
        Assert.True(cell.Unprepared is null, cell.Describe());
    }

    /// <summary>
    /// The fourth level between the frameworks (decision 089): the artifact of the target -
    /// Dapper's among them - returns the rows the catalog's own text returns over ADO.NET with
    /// the catalog's values (decision 209), in the canonical form of decision 170. A
    /// disagreement is a wrong translation, a difference the frameworks make that the text does
    /// not, or a slice of the author's text that does not determine its rows - the text stays
    /// the author's, and the record says which it was.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cells))]
    public void EveryTargetReturnsTheRowsOfTheSource(string key, ORMEnum framework)
    {
        var run = SedeRun.Current;
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
    /// A disagreement is a change of the text, of the values or of the data that a new judge has
    /// to document, never something to be fixed in the comparison. Until a judge exists the case
    /// skips and says that this run can make one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void TheSourceReturnsTheRowsOfTheJudge(string key)
    {
        var run = SedeRun.Current;
        run.SkipIfUnavailable();
        run.SkipIfNotCovered(key);

        if (!run.HasJudge)
        {
            Assert.Skip("The Sede database holds no judge yet; the source rows of this run (judge-candidate.tsv in the results directory) become one once the run is documented (decision 208).");
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
        var run = SedeRun.Current;
        run.SkipIfUnavailable();

        Assert.Equal(SedeSample.DataSet, run.DataSet);
        foreach (var line in run.Describe())
        {
            output.WriteLine(line);
        }
    }
}
