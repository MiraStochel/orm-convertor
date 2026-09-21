using Model;
using Tests.Database;

namespace Tests.Differential;

/// <summary>
/// The fourth verification level over a query (decisions 016 and 089): the generated query
/// is executed against the fixture and what came back is compared, field by field, with the
/// canonical result of that query.
///
/// Both halves of a pair really run - the source variant here or in the Java suite, the
/// translated one in the suite that owns its framework - and they meet over the canonical
/// file rather than across a process boundary, because decision 089 refused to grow an
/// endpoint that runs foreign code for the sake of a test.
///
/// The canonical result belongs to the query and not to a direction, which is what keeps
/// it honest: a translation broken in one direction cannot be fixed by moving the target,
/// because moving it breaks every other direction of the same query at once.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DifferentialVerificationTest(TestSchemaFixture fixture)
{
    /// <summary>
    /// Where a recording run writes the canonical results. It is a path and not a flag so
    /// that recording cannot happen by accident, and only the source variant ever writes:
    /// a result recorded from a translation would make the translation its own judge.
    /// </summary>
    private const string RecordVariable = "ORMCONVERTOR_RECORD_DIFFERENTIAL";

    /// <summary>The queries whose source framework this suite can run.</summary>
    public static TheoryData<string> SourceQueries()
    {
        var data = new TheoryData<string>();
        foreach (var query in DifferentialMatrix.Queries.Where(q => DotNetQueryRunner.Owns(q.Source)))
        {
            data.Add(query.Id);
        }

        return data;
    }

    /// <summary>
    /// The pairs of the matrix whose target this suite can run. The rest are the Java
    /// suite's, and each suite states that it ran every pair assigned to it
    /// (<see cref="DifferentialMatrixTest"/>), so neither half can be met by skipping.
    /// </summary>
    public static TheoryData<string, ORMEnum> Pairs()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var (query, target) in DifferentialMatrix.Pairs().Where(p => DotNetQueryRunner.Owns(p.Target)))
        {
            data.Add(query.Id, target);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SourceQueries))]
    public void TheSourceVariantFixesTheCanonicalResult(string id)
    {
        fixture.SkipIfUnavailable();

        var query = Query(id);
        var rows = DotNetQueryRunner.Run(query, query.Source, fixture);

        if (Recording() is { } directory)
        {
            Record(directory, query, rows);
            return;
        }

        Assert.Equal(query.CanonicalResult(), rows);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheTranslatedVariantMatchesTheCanonicalResult(string id, ORMEnum target)
    {
        fixture.SkipIfUnavailable();

        if (Recording() is not null)
        {
            Assert.Skip("A recording run fixes the canonical results; comparing against them is the next run.");
        }

        var query = Query(id);
        var rows = DotNetQueryRunner.Run(query, target, fixture);

        Assert.Equal(query.CanonicalResult(), rows);
    }

    internal static DifferentialQuery Query(string id)
        => DifferentialMatrix.Queries.Single(query => query.Id == id);

    private static string? Recording()
    {
        var directory = Environment.GetEnvironmentVariable(RecordVariable);
        return string.IsNullOrWhiteSpace(directory) ? null : directory;
    }

    private static void Record(string directory, DifferentialQuery query, IEnumerable<string> rows)
    {
        var results = Path.Combine(directory, "results");
        Directory.CreateDirectory(results);

        // CRLF because the repository stores these as text files under its own line-ending
        // rule; the comparison is by line, so the choice affects the diff and nothing else.
        File.WriteAllText(
            Path.Combine(results, query.Id + ".txt"),
            string.Concat(rows.Select(row => row + "\r\n")));
    }
}
