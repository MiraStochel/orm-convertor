using System.Globalization;
using Model;
using SampleData;
using Tests.Differential;

namespace Tests.LdbcJudge;

/// <summary>
/// The fourth verification level over the LDBC catalog, judged by LDBC's own validation set
/// for Interactive v1 (decision 117). Until now "translated as specified" was the claim of the
/// author who wrote the texts, and the differential matrix could not test it: it compares a
/// target with its source, so a text that read the specification wrongly would make all six
/// targets wrong alike and the matrix green. Here the expected rows come from LDBC.
///
/// The source run - Dapper with the catalog's own text - judges the text: a disagreement there
/// means the text reads the specification wrongly. A target that disagrees where the source
/// agrees has a wrong translation. Every read of a query translated as specified has to agree,
/// in every framework; a query translated with a simplification is measured instead - the share
/// of agreeing reads goes to the output of the run, because how often the simplification shows
/// is the number, and tolerances written from its note would be the judge rewritten by hand.
///
/// The replay runs once, for the first test that asks for it (<see cref="LdbcReplay"/>), and
/// takes minutes over SF 0.1 and longer over SF 1; without a configured LdbcSnb every test here
/// skips with the reason, and fails where ORMCONVERTOR_REQUIRE_LDBC_DATABASE promised one.
/// The Java suite judges Hibernate, EclipseLink and MyBatis over the same set the same way.
/// </summary>
public class LdbcValidationTest(ITestOutputHelper output)
{
    public static TheoryData<string, ORMEnum> AsSpecified() => Cells(LdbcTranslation.AsSpecified);

    public static TheoryData<string, ORMEnum> Simplified() => Cells(LdbcTranslation.Simplified);

    public static TheoryData<string, ORMEnum> Mutations()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var mutation in DifferentialMutation.All())
        {
            foreach (var framework in LdbcReplay.Frameworks)
            {
                data.Add(mutation.Key, framework);
            }
        }

        return data;
    }

    /// <summary>
    /// The claim behind "translated as specified": every read of the query's operation returns
    /// what LDBC expects - from the text itself and from its translation into this framework.
    /// </summary>
    [Theory]
    [MemberData(nameof(AsSpecified))]
    public void EveryReadOfAQueryTranslatedAsSpecifiedAgreesWithTheJudge(string key, ORMEnum framework)
    {
        var replay = LdbcReplay.Current;
        replay.SkipIfUnavailable();

        var tally = replay.Tally(key, framework);
        var cell = $"{key} in {framework}{(framework == ORMEnum.Dapper ? " (the catalog's text)" : string.Empty)}";

        Assert.True(tally.Unprepared is null, tally.Describe(cell));
        SkipIfThePrefixHoldsNoRead(replay, tally, key);
        Assert.True(tally.Matches == tally.Reads, tally.Describe(cell));
    }

    /// <summary>
    /// A query with a simplification is run against the judge as well, and what it yields is a
    /// number, not a verdict: the share of reads that agree. Its artifact still has to run - a
    /// read that fails is a failure of the translation, whatever the note says.
    /// </summary>
    [Theory]
    [MemberData(nameof(Simplified))]
    public void AQueryWithASimplificationIsMeasuredAgainstTheJudge(string key, ORMEnum framework)
    {
        var replay = LdbcReplay.Current;
        replay.SkipIfUnavailable();

        var tally = replay.Tally(key, framework);
        var cell = $"{key} in {framework}";

        Assert.True(tally.Unprepared is null, tally.Describe(cell));
        SkipIfThePrefixHoldsNoRead(replay, tally, key);
        Assert.True(tally.Failures == 0, tally.Describe(cell));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{cell}: {tally.Matches} of {tally.Reads} reads agree with the judge ({100.0 * tally.Matches / tally.Reads:F1} %), set {replay.SetName}."));
        foreach (var disagreement in tally.Disagreements)
        {
            output.WriteLine("  " + disagreement);
        }
    }

    /// <summary>
    /// The negative half: a deliberately wrong artifact of one query (the mutations of decision
    /// 089) disagrees with the judge on some read, or cannot even run - the comparison reacts
    /// to an error. The judge is independent of the tool, so no fixture had to be designed
    /// against the mutations, as decision 089 had to.
    /// </summary>
    [Theory]
    [MemberData(nameof(Mutations))]
    public void AMutatedArtifactDisagreesWithTheJudge(string mutation, ORMEnum framework)
    {
        var replay = LdbcReplay.Current;
        replay.SkipIfUnavailable();

        var tally = replay.Mutation(mutation, framework);

        Assert.True(tally.Changed, $"The mutation \"{mutation}\" changed nothing in the {framework} artifact of {LdbcReplay.MutatedQuery}.");
        if (tally.Detection is null && tally.Reads == 0 && LdbcDatabase.Rows is not null)
        {
            Assert.Skip($"The prefix of {LdbcDatabase.Rows} lines holds no read of {LdbcReplay.MutatedQuery}.");
        }

        Assert.True(
            tally.Detection is not null,
            $"The mutation \"{mutation}\" of the {framework} artifact of {LdbcReplay.MutatedQuery} agreed with the judge on all {tally.Reads} reads.");
        output.WriteLine($"{mutation} in {framework}: {tally.Detection}");
    }

    /// <summary>
    /// The compensation returns the database to the state it was loaded in: every LDBC table
    /// holds as many rows after the replay as before it - and, where the replay inserted
    /// anything, more in between, so that the equality is not that of a replay that did nothing.
    /// </summary>
    [Fact]
    public void TheCompensationReturnsTheDatabaseToItsLoadedState()
    {
        var replay = LdbcReplay.Current;
        replay.SkipIfUnavailable();

        Assert.Equal(replay.Loaded, replay.Restored);

        if (replay.Inserts > 0)
        {
            Assert.Contains(replay.Replayed, table => table.Value > replay.Loaded[table.Key]);
        }

        var extent = LdbcDatabase.Rows is { } rows ? $"a prefix of {rows}" : "the whole set";
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{replay.SetName}: {replay.Lines} lines replayed ({replay.Inserts} inserts) in {replay.Elapsed.TotalMinutes:F1} min, {extent}."));
    }

    /// <summary>
    /// Without a configured LdbcSnb the judge skips with its reason, because the tool and most
    /// of the suite need no database; where the environment states that it provides one, the
    /// same reason is a failure, as decision 039 has it for the test database.
    /// </summary>
    [Fact]
    public void AMissingJudgeSkipsUnlessTheEnvironmentPromisedIt()
    {
        // The decision is asserted, not the skip itself: xUnit marks a test skipped the moment
        // Assert.Skip runs, even where the exception is caught.
        Assert.Null(LdbcDatabase.FailureOf("no LdbcSnb", required: false));

        var failure = LdbcDatabase.FailureOf("no LdbcSnb", required: true);
        Assert.NotNull(failure);
        Assert.Contains("ORMCONVERTOR_REQUIRE_LDBC_DATABASE", failure);
        Assert.Contains("no LdbcSnb", failure);
    }

    /// <summary>A prefix is a positive number of lines; anything else stops the run rather than replaying something else.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("2000", 2000)]
    public void ThePrefixIsAPositiveNumberOfLines(string? configured, int? rows)
        => Assert.Equal(rows, LdbcDatabase.ResolveRows(configured));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("many")]
    public void APrefixThatIsNoNumberOfLinesIsRefused(string configured)
        => Assert.Throws<InvalidOperationException>(() => LdbcDatabase.ResolveRows(configured));

    private static TheoryData<string, ORMEnum> Cells(LdbcTranslation translation)
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var query in LdbcReplay.Judged().Where(query => query.Translation == translation))
        {
            foreach (var framework in LdbcReplay.Frameworks)
            {
                data.Add(query.Key, framework);
            }
        }

        return data;
    }

    /// <summary>
    /// A prefix may end before the first read of a query; that is a smaller judge, not a failure.
    /// The whole set reads every query thousands of times, so there the same is a failure.
    /// </summary>
    private static void SkipIfThePrefixHoldsNoRead(LdbcReplay replay, LdbcTally tally, string key)
    {
        if (tally.Reads > 0)
        {
            return;
        }

        Assert.True(LdbcDatabase.Rows is not null, $"The whole set {replay.SetName} was replayed and holds no read of {key}.");
        Assert.Skip($"The prefix of {LdbcDatabase.Rows} lines holds no read of {key}.");
    }
}
