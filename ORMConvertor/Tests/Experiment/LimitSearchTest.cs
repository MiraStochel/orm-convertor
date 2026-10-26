using Model;
using Tests.Combined;
using Tests.Database;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// The search for limits of the .NET suite (decision 182): a run on demand, never a test of a
/// build. With <c>ORMCONVERTOR_EXPERIMENT_RESULTS</c> set, every axis of <see cref="LimitSeries"/>
/// is walked from every source that states it, in a child process per axis and source, and the
/// points go into <c>series-points.tsv</c> under the directory of the run - the rows of the path
/// with the time and the memory of the translation beside them -, the inputs of the points the
/// walk reached into <c>inputs/</c>, where the Java half of the experiment run
/// (<c>ORMCONVERTOR_EXPERIMENT_KINDS=series</c>) and the protocol of decision 175 find them. The
/// summary of the run reads the points into its section on limits. Without the variable the
/// one case skips with the reason; no point fails the run - a refusal, a crash and a point over
/// the bound are rows -, only a walk that makes no progress fails, by name.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class LimitSearchTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private const string None = "";

    public static TheoryData<string, ORMEnum> Walks()
    {
        var data = new TheoryData<string, ORMEnum>();
        if (ExperimentRun.ResultsDirectory is null)
        {
            data.Add(None, ORMEnum.Dapper);
            return data;
        }

        foreach (var axis in LimitSearch.Axes)
        {
            foreach (var source in axis.Sources)
            {
                data.Add(axis.Id, source);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Walks))]
    public void TheSeriesIsWalkedToItsLimit(string axisId, ORMEnum source)
    {
        if (axisId.Length == 0)
        {
            Assert.Skip($"No run configured: set {ExperimentRun.ResultsVariable} to the directory of the run (decision 182).");
        }

        var results = ExperimentRun.ResultsDirectory!;
        var inputs = ExperimentRun.InputsDirectory!;
        var axis = LimitSeries.Axis(axisId)!;

        // The points of a query series run over the fixture, and a Dapper or MyBatis source takes
        // its keys from it; the walk needs the database the collection holds.
        if (!fixture.IsAvailable)
        {
            Assert.Fail($"The walk of {axis.Id} from {source} needs the test database as its catalog and to run over, and it is not available: {fixture.SkipReason}");
        }

        var points = LimitSearch.Drive(axis, source, results, inputs, LimitSearch.Catalog, output.WriteLine);

        foreach (var target in Enum.GetValues<ORMEnum>())
        {
            var walked = points.Where(point => point.Verdict.Target == target.ToString()).OrderBy(point => point.Point).ToList();
            var last = walked.LastOrDefault(point => point.Step == SeriesPoint.Pass);
            var stop = walked.FirstOrDefault(point => point.Step != SeriesPoint.Pass);
            output.WriteLine($"{axis.Id} {source} -> {target}: last passed {last?.Point.ToString() ?? "none"}, "
                + (stop is null ? "ceiling reached" : $"stopped at {stop.Point}: {stop.Verdict.Outcome}"));
        }

        Assert.NotEmpty(points);
    }
}

/// <summary>
/// The child process of the search for limits: the walk of one axis from one source, named by
/// <c>ORMCONVERTOR_SERIES_WALK</c>, into the file <c>ORMCONVERTOR_SERIES_FILE</c>. Deliberately
/// outside the collection of the fixture - the parent holds the schema, and a child that created
/// and dropped it would pull it from under the parent -, and the only class a child runs. Without
/// the variable it skips, which is what a build sees of it.
/// </summary>
public class LimitSeriesChildTest(ITestOutputHelper output)
{
    [Fact]
    public void TheWalkOfTheChildProcess()
    {
        var walk = Environment.GetEnvironmentVariable(LimitSearch.WalkVariable);
        if (string.IsNullOrWhiteSpace(walk))
        {
            Assert.Skip($"Not a child of the search for limits: {LimitSearch.WalkVariable} is not set (decision 182).");
        }

        var parts = walk.Split('|');
        if (parts.Length != 2 || LimitSeries.Axis(parts[0]) is not { } axis || !Enum.TryParse<ORMEnum>(parts[1], out var source))
        {
            throw new InvalidOperationException($"{LimitSearch.WalkVariable} is \"{walk}\"; it names a walk as axis|Source.");
        }

        var file = Environment.GetEnvironmentVariable(LimitSearch.FileVariable);
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new InvalidOperationException($"{LimitSearch.FileVariable} names the file of points the walk writes, and it is not set.");
        }

        var inputs = Environment.GetEnvironmentVariable(LimitSearch.InputsVariable);
        LimitSearch.Walk(axis, source, file.Trim(), string.IsNullOrWhiteSpace(inputs) ? null : inputs.Trim(), LimitSearch.Catalog, log: output.WriteLine);
    }
}

/// <summary>
/// What a build holds about the series (decision 182): every series starts translated in every
/// direction - a limit found at a larger point is then the tool's, not the generator's -, a point
/// carries as many of its thing as it says, a key of a point parses and a point nobody has is
/// refused by name, the walk writes the rows the summary reads, and the Java suite names the
/// same series as having no fourth level.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class LimitSeriesTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    public static TheoryData<string, ORMEnum> Starts()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var axis in LimitSeries.Axes)
        {
            foreach (var source in axis.Sources)
            {
                data.Add(axis.Id, source);
            }
        }

        return data;
    }

    /// <summary>
    /// The smallest point of every series translates from every source that states it into every
    /// target without a refusal, with the fixture as the catalog - the keys of a Dapper or MyBatis
    /// source come from it -; a query series yields a query artifact. The second and the third
    /// level of the first point are the walk's, with the rest of the series.
    /// </summary>
    [Theory]
    [MemberData(nameof(Starts))]
    public void TheSmallestPointTranslatesInEveryDirection(string axisId, ORMEnum source)
    {
        fixture.SkipIfUnavailable();

        var axis = LimitSeries.Axis(axisId)!;
        var units = axis.Units(source, axis.Points[0]);

        foreach (var target in Enum.GetValues<ORMEnum>())
        {
            var conversion = OrmConvertor.ConversionHandler.Convert(source, target, units, fixture.CatalogReader);
            var failures = ExperimentRun.Failures(conversion);

            Assert.True(failures.Count == 0, $"{axis.Id} {axis.Points[0]} {source} -> {target} is refused: {string.Join("; ", failures)}");
            if (axis.OverTheFixture)
            {
                Assert.Contains(conversion.Sources, artifact => artifact.ContentType.IsQuery());
            }
        }
    }

    public static TheoryData<ORMEnum> ExpressionSources()
    {
        var data = new TheoryData<ORMEnum>();
        foreach (var source in LimitSeries.Axis("expression")!.Sources)
        {
            data.Add(source);
        }

        return data;
    }

    /// <summary>
    /// A sum of dozens of terms in the projection translates from every source into every target
    /// within the 30 s of S3. The typing of an expression once derived the scalar of every node
    /// again at each level above it, which doubled the time with every term: 16 terms took a
    /// second and 32 did not finish in two hours. A direction over the bound is left running
    /// rather than awaited, so that the test fails at the bound instead of hanging.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExpressionSources))]
    public async Task ASumOfDozensOfTermsTranslatesInEveryDirectionWithinTheS3Bound(ORMEnum source)
    {
        fixture.SkipIfUnavailable();

        const int terms = 64;
        var units = LimitSeries.Axis("expression")!.Units(source, terms);

        foreach (var target in Enum.GetValues<ORMEnum>())
        {
            var translation = Task.Run(() => OrmConvertor.ConversionHandler.Convert(source, target, units, fixture.CatalogReader), TestContext.Current.CancellationToken);
            var first = await Task.WhenAny(translation, Task.Delay(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            Assert.True(first == translation, $"A sum of {terms} terms {source} -> {target} did not translate within the 30 s of S3.");

            var conversion = await translation;
            var failures = ExperimentRun.Failures(conversion);
            Assert.True(failures.Count == 0, $"A sum of {terms} terms {source} -> {target} is refused: {string.Join("; ", failures)}");
            Assert.Contains(conversion.Sources, artifact => artifact.ContentType.IsQuery());
        }
    }

    [Fact]
    public void APointCarriesAsManyAsItSays()
    {
        string Sql(string axis, int point) => LimitSeries.Axis(axis)!.Units(ORMEnum.Dapper, point).Last().Content;
        string EFCore(string axis, int point) => LimitSeries.Axis(axis)!.Units(ORMEnum.EFCore, point).Last().Content;

        Assert.Equal(16, Occurrences(Sql("joins", 16), "INNER JOIN"));
        Assert.Equal(15, Occurrences(Sql("conditions", 16), " AND "));
        Assert.Equal(16, Occurrences(Sql("nesting", 16), "("));
        Assert.Equal(17, Occurrences(Sql("subqueries", 16), "SELECT "));
        Assert.Equal(16, Occurrences(Sql("exists", 16), "EXISTS ("));
        Assert.Equal(16, Occurrences(Sql("ctes", 16), " AS (SELECT "));
        Assert.Equal(16, Occurrences(Sql("unions", 16), " UNION "));
        Assert.Equal(16, Occurrences(Sql("expression", 16), " + "));
        Assert.Equal(17, Occurrences(EFCore("properties", 16), "{ get; set; }"));
        Assert.Equal(16, Occurrences(EFCore("relations", 16), "[ForeignKey("));
        Assert.Contains("nameof(K16)", EFCore("key-parts", 16));
        var plain = QueryShapeInputs.MappingUnits(ORMEnum.Dapper)[0].Content.Length;
        Assert.InRange(LimitSeries.Axis("unit-size")!.Units(ORMEnum.Dapper, 64)[0].Content.Length, 64 * 1024 + plain, 64 * 1024 + plain + 100);
        Assert.Equal(200, LimitSeries.Axis("entities")!.Units(ORMEnum.Hibernate, 100).Count);
    }

    [Fact]
    public void TheKeyOfAPointParsesAndAPointNobodyHasIsRefusedByName()
    {
        var key = ForeignKey.Parse("series:joins-16");
        Assert.Equal(ForeignKey.Series, key.Kind);

        var point = LimitSeries.Parse(key.Id);
        Assert.NotNull(point);
        Assert.Equal("joins", point.Value.Axis.Id);
        Assert.Equal(16, point.Value.Point);

        Assert.Null(LimitSeries.Parse("joins-17"));
        Assert.Null(LimitSeries.Parse("no-such-axis-1"));

        foreach (var axis in LimitSeries.Axes)
        {
            Assert.All(axis.Points, p => Assert.Equal((axis, p), LimitSeries.Parse(axis.Key(p).Id)!.Value));
            Assert.NotEmpty(axis.Sources);
            Assert.True(axis.Points.Count > 1, $"{axis.Id} has one point; a series grows.");
        }
    }

    /// <summary>
    /// The walk in this process over a series cut to its first two points, Dapper into EF Core:
    /// two rows, both passed, both at the fourth level, with the time and the memory of the
    /// translation and the bytes of the input; the input of each point exported where the
    /// experiment run finds it; a second walk over the same file adds nothing.
    /// </summary>
    [Fact]
    public void TheWalkWritesAPointPerStepAndStopsWhereItStood()
    {
        fixture.SkipIfUnavailable();

        var temp = Directory.CreateTempSubdirectory("ormconvertor-series-");
        try
        {
            var axis = LimitSeries.Axis("conditions")! with { Points = [1, 2] };
            var file = Path.Combine(temp.FullName, LimitSearch.FileName);
            var inputs = Path.Combine(temp.FullName, "inputs");

            LimitSearch.Walk(axis, ORMEnum.Dapper, file, inputs, LimitSearch.CatalogFixture, [ORMEnum.EFCore], output.WriteLine);

            var points = SeriesPoint.Read(file);
            Assert.Equal(2, points.Count);
            Assert.All(points, point =>
            {
                Assert.Equal(SeriesPoint.Pass, point.Step);
                Assert.Equal(ForeignLevel.Ran, point.Verdict.Reached);
                Assert.Equal(ForeignVerdict.Ran, point.Verdict.Outcome);
                Assert.True(point.TranslateMs > 0);
                Assert.True(point.InputBytes > 0);
                Assert.Equal(LimitSearch.CatalogFixture, point.Catalog);
            });

            var exported = ExperimentInput.Discover(inputs, TestDatabase.SchemaName);
            Assert.Equal(2, exported.Count);
            Assert.All(exported, input => Assert.Equal(ForeignKey.Series, input.Key.Kind));

            LimitSearch.Walk(axis, ORMEnum.Dapper, file, inputs, LimitSearch.CatalogFixture, [ORMEnum.EFCore]);
            Assert.Equal(2, SeriesPoint.Read(file).Count);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>A point of a series goes through the path as a key of its own: judged to the fourth level over the fixture, as a foreign artifact of the point would be.</summary>
    [Fact]
    public void APointOfASeriesIsJudgedOverTheFixture()
    {
        fixture.SkipIfUnavailable();

        var axis = LimitSeries.Axis("joins")!;
        var input = new ExperimentInput("series-joins-2-EFCore", axis.Key(2), ORMEnum.EFCore, axis.Units(ORMEnum.EFCore, 2));

        var (verdict, _) = ExperimentRun.Judge(input, ORMEnum.NHibernate, fixture);
        output.WriteLine(verdict.Describe());

        Assert.Equal(ForeignLevel.Ran, verdict.Reached);
        Assert.Equal(ForeignVerdict.Ran, verdict.Outcome);
        Assert.NotNull(verdict.Rows);
    }

    /// <summary>The summary reads the points of a run into its section on limits: the last point that passed per direction, where the series stopped and why.</summary>
    [Fact]
    public void TheSummaryReadsThePointsOfARun()
    {
        var temp = Directory.CreateTempSubdirectory("ormconvertor-series-summary-");
        try
        {
            var axis = LimitSeries.Axis("joins")!;
            List<SeriesPoint> points =
            [
                Point(axis, 1, ORMEnum.Dapper, ORMEnum.EFCore, SeriesPoint.Pass, ForeignLevel.Ran, ForeignVerdict.Ran, 12),
                Point(axis, 2, ORMEnum.Dapper, ORMEnum.EFCore, SeriesPoint.Pass, ForeignLevel.Accepted, ForeignVerdict.Failed, 15),
                Point(axis, 4, ORMEnum.Dapper, ORMEnum.EFCore, SeriesPoint.Fail, ForeignLevel.None, ForeignVerdict.Refused, 20),
                Point(axis, 1, ORMEnum.Dapper, ORMEnum.Hibernate, SeriesPoint.Pass, ForeignLevel.None, SeriesPoint.Translated, 9),
                Point(axis, 2, ORMEnum.Dapper, ORMEnum.Hibernate, SeriesPoint.Crash, ForeignLevel.None, "crashed", 0),
            ];
            SeriesPoint.Write(Path.Combine(temp.FullName, LimitSearch.FileName), points);
            File.WriteAllText(
                Path.Combine(temp.FullName, ExperimentRun.JavaRowsFileName),
                ForeignVerdict.Header + "\n" + string.Join('\t', "series-joins-1-Dapper-Hibernate", "series:joins-1", "Dapper", "Hibernate", "tool", "target", "4", "ran", "", "3", "", "1", "2", "3", "4", "{}") + "\n");

            var lines = ExperimentSummary.Summarize(temp.FullName);
            var text = string.Join('\n', lines);
            output.WriteLine(text);

            Assert.Contains("## Limits", text);
            Assert.Contains("| Dapper → EFCore | 2 | 2 | 1 | 4: refused |", text);
            Assert.Contains("| Dapper → Hibernate | 1 | 1 | 1 | 2: crashed |", text);
            Assert.DoesNotContain("series | ", text);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>The Java suite names the series without a fourth level - the ones over no table of the fixture - as this suite does; one list, held against the Java source.</summary>
    [Fact]
    public void TheJavaSuiteNamesTheSameSeriesAsHavingNoFourthLevel()
    {
        var source = File.ReadAllText(Path.Combine(
            TargetFrameworkDescriptorTest.SolutionDirectory(),
            "JavaTests", "src", "test", "java", "cz", "stochel", "ormconvertor", "javatests", "foreign", "ForeignArtifactRun.java"));

        var declared = System.Text.RegularExpressions.Regex.Match(source, "SERIES_WITHOUT_A_FOURTH_LEVEL\\s*=\\s*(?:java\\.util\\.)?Set\\.of\\(([^)]*)\\)");
        Assert.True(declared.Success, "The Java suite declares SERIES_WITHOUT_A_FOURTH_LEVEL in ForeignArtifactRun.java.");

        var java = declared.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(name => name.Trim('"')).Order(StringComparer.Ordinal);
        var dotnet = LimitSeries.Axes.Where(axis => !axis.OverTheFixture).Select(axis => axis.Id).Order(StringComparer.Ordinal);
        Assert.Equal(dotnet, java);
    }

    private static SeriesPoint Point(LimitAxis axis, int point, ORMEnum source, ORMEnum target, string step, int reached, string outcome, double ms) => new()
    {
        Verdict = new ForeignVerdict
        {
            Artifact = LimitSearch.PointId(axis, point, source, target),
            Answers = axis.Key(point).ToString(),
            Source = source.ToString(),
            Target = target.ToString(),
            Producer = ExperimentRun.Producer,
            Language = ForeignArtifact.LanguageTarget,
            Metadata = "{}",
            Reached = reached,
            Outcome = outcome,
        },
        Axis = axis.Id,
        Point = point,
        Catalog = LimitSearch.CatalogFixture,
        Step = step,
        TranslateMs = ms,
        AllocatedKb = 2048,
        InputBytes = 100,
    };

    private static int Occurrences(string text, string word)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += word.Length;
        }

        return count;
    }
}
