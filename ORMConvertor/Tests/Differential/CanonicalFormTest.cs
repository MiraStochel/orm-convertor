namespace Tests.Differential;

/// <summary>
/// The canonical form of a whole result (decision 170), the case the differential verification
/// documents about its comparison: the order inside a run of rows that agree on the ordering key
/// is not compared, the order of the key values is, and a query without an ordering is a set.
/// What a query determines is compared and nothing more - and nothing less: a translation that
/// dropped the ordering still differs, because the sequence of key values is still held.
/// </summary>
public class CanonicalFormTest
{
    private static readonly ResultRow.RenderSettings Settings = new();
    private static readonly string[] Fields = ["Score", "Name"];

    /// <summary>Rows of one key value are the same result in whichever order they came, as the data of 2026-10-23 made q31 of FIBEN return them.</summary>
    [Fact]
    public void RowsOfOneKeyValueCompareAsASet()
    {
        var first = ResultRow.Canonical(Rows([60, "A"], [53, "C"], [53, "B"]), Settings, Fields, ["Score"]);
        var second = ResultRow.Canonical(Rows([60, "A"], [53, "B"], [53, "C"]), Settings, Fields, ["Score"]);

        Assert.Equal(first, second);
        Assert.Equal(["60\t\"A\"", "53\t\"B\"", "53\t\"C\""], first);
    }

    /// <summary>The sequence of key values is what the query determines, so another sequence is another result.</summary>
    [Fact]
    public void AnotherOrderOfTheKeyValuesIsAnotherResult()
    {
        var ordered = ResultRow.Canonical(Rows([60, "A"], [53, "B"]), Settings, Fields, ["Score"]);
        var reversed = ResultRow.Canonical(Rows([53, "B"], [60, "A"]), Settings, Fields, ["Score"]);

        Assert.NotEqual(ordered, reversed);
    }

    /// <summary>A key the result does not show is the whole row, and the rows are compared as they came.</summary>
    [Fact]
    public void TheWholeRowAsTheKeyKeepsTheRowsAsTheyCame()
    {
        var rows = ResultRow.Canonical(Rows([53, "B"], [60, "A"]), Settings, Fields, Fields);

        Assert.Equal(["53\t\"B\"", "60\t\"A\""], rows);
    }

    /// <summary>Without an ordering the whole result is a set.</summary>
    [Fact]
    public void WithoutAKeyTheWholeResultIsASet()
    {
        var rows = ResultRow.Canonical(Rows([60, "A"], [53, "B"]), Settings, Fields, []);

        Assert.Equal(["53\t\"B\"", "60\t\"A\""], rows);
    }

    /// <summary>A key that names a field the row has not is a mistake of the catalog, and it stops the run rather than comparing by a guess.</summary>
    [Fact]
    public void AKeyOutsideTheRowIsRefused()
    {
        var refused = Assert.Throws<InvalidOperationException>(
            () => ResultRow.Canonical(Rows([60, "A"]), Settings, Fields, ["Rank"]));

        Assert.Contains("170", refused.Message);
    }

    private static IReadOnlyList<object?>[] Rows(params IReadOnlyList<object?>[] rows) => rows;
}
