namespace Tests.Differential;

/// <summary>
/// That both ecosystems render a row into the same text (decision 089). It is the first
/// thing the differential verification needs, because every comparison it makes rests on
/// it: decimal and BigDecimal print trailing zeros differently, LocalDateTime and DateTime
/// format midnight differently, and a tab inside a string breaks the separator if one side
/// forgets to escape it.
///
/// Neither suite records the expected text. It is checked in, both render against it, and
/// a disagreement therefore shows up as a failure on the side that drifted rather than as
/// two suites quietly agreeing on something new.
/// </summary>
public class RendererConformanceTest
{
    [Fact]
    public void RenderedRowsMatchTheConformanceText()
    {
        var expected = DifferentialData.ReadLines("renderer-conformance.txt");

        var actual = RendererConformance.Rows
            .Select(row => ResultRow.Render(row, RendererConformance.Settings))
            .ToList();

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// A type nobody wrote a rule for has to stop the run. The canonical form is only worth
    /// something while every value in it got there by a stated rule, so the renderer guesses
    /// at nothing - not even at something as obvious as a Guid.
    /// </summary>
    [Fact]
    public void AValueWithNoRuleIsRefused()
    {
        var refused = Assert.Throws<NotSupportedException>(
            () => ResultRow.RenderField(Guid.Empty, RendererConformance.Settings));

        Assert.Contains("089", refused.Message);
    }
}
