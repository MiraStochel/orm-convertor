using System.Globalization;

namespace Tests.Differential;

/// <summary>
/// The canonical text form of one row of a query result (decision 089). Both ecosystems
/// render into it and the comparison is of the rendered text, because a pair whose halves
/// run in different runtimes cannot meet anywhere else - the decision rejected an endpoint
/// that would run foreign code for exactly that purpose.
///
/// Every rule below has a counterpart in <c>ResultRow.java</c>, and that the two agree is
/// not assumed: <c>renderer-conformance.txt</c> is rendered by both suites and compared
/// against the same checked-in text. It is the first thing to run, because everything else
/// here rests on it.
///
/// A type the rules do not name throws rather than guessing. Guessing is what the whole
/// decision is against: a value rendered by a rule nobody wrote would make two suites agree
/// or disagree for a reason no one could read out of the file.
/// </summary>
internal static class ResultRow
{
    /// <summary>What a null field renders as. No string can collide with it: strings are quoted.</summary>
    public const string Null = "NULL";

    /// <summary>Separator between fields. A tab inside a string value is escaped, so it cannot occur unescaped.</summary>
    public const char Separator = '\t';

    /// <summary>
    /// How much of a value survives rendering. It is a property of what the query computes -
    /// an average over money bears a different scale than a plain column - so it travels with
    /// the query in the matrix, not with the host the suite runs on (decision 089).
    /// </summary>
    /// <param name="DecimalScale">Decimal places an exact decimal is rounded to, half to even.</param>
    /// <param name="FloatSignificantDigits">
    /// Significant digits an approximate number is rounded to. Binary floating point differs
    /// between the ecosystems in the last bits, and insisting on those would measure the
    /// driver rather than the translation.
    /// </param>
    /// <param name="FractionalSecondDigits">Fractional digits of a timestamp, as the mapping states them.</param>
    /// <remarks>
    /// A record class and not a record struct, and that is not a matter of taste: on a
    /// struct <c>new()</c> is the zero-initializing constructor, so the defaults written
    /// here would never run and every caller who did not spell all three out would quietly
    /// render at scale zero.
    /// </remarks>
    public sealed record RenderSettings(
        int DecimalScale = 6,
        int FloatSignificantDigits = 12,
        int FractionalSecondDigits = 3);

    /// <summary>Renders one row: its fields in the order the matrix states, joined by the separator.</summary>
    public static string Render(IReadOnlyList<object?> fields, RenderSettings settings)
        => string.Join(Separator, fields.Select(field => RenderField(field, settings)));

    /// <summary>
    /// Orders rendered rows the way a query without an ordering instruction has to be
    /// compared: as a set, by the rendered line itself. Ordinal, because the Java side
    /// compares UTF-16 code units and nothing else would agree with it.
    /// </summary>
    public static List<string> Sorted(IEnumerable<string> rows)
        => [.. rows.OrderBy(row => row, StringComparer.Ordinal)];

    public static string RenderField(object? value, RenderSettings settings) => value switch
    {
        null or DBNull => Null,

        bool flag => flag ? "true" : "false",

        // Every integral width renders the same way: an integer has no facets to lose.
        sbyte or byte or short or ushort or int or uint or long or ulong
            => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),

        decimal exact => RenderDecimal(exact, settings.DecimalScale),

        float approximate => RenderApproximate(approximate, settings.FloatSignificantDigits),
        double approximate => RenderApproximate(approximate, settings.FloatSignificantDigits),

        DateTime timestamp => RenderTimestamp(timestamp, settings.FractionalSecondDigits),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),

        string text => RenderString(text),

        _ => throw new NotSupportedException(
            $"A value of type {value.GetType().FullName} has no rendering rule in decision 089, "
            + "so the canonical form cannot state it. Add the rule to both suites and to the "
            + "conformance file, or keep the type out of the matrix - do not let this guess."),
    };

    /// <summary>
    /// An exact decimal at the stated scale, rounded half to even. The scale is stated rather
    /// than taken from the value because the same column arrives with different scales from
    /// different drivers, and the comparison must not depend on that.
    /// </summary>
    private static string RenderDecimal(decimal value, int scale)
        => Math.Round(value, scale, MidpointRounding.ToEven)
            .ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    /// An approximate number rounded to the stated significant digits and then written
    /// without an exponent and without trailing zeros. The last step is what makes the two
    /// ecosystems agree: one of them would otherwise write 3 and the other 3.0.
    /// </summary>
    private static string RenderApproximate(double value, int significantDigits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new NotSupportedException(
                $"The value {value} is not a number the canonical form can state (decision 089).");
        }

        decimal exact;
        try
        {
            exact = (decimal)value;
        }
        catch (OverflowException)
        {
            throw new NotSupportedException(
                $"The value {value} is outside the range the canonical form renders (decision 089).");
        }

        if (exact != 0m)
        {
            // Significant digits, not decimal places: the exponent of the value decides how
            // many places that is. Clamped to what decimal rounding accepts at either end.
            var exponent = (int)Math.Floor(Math.Log10((double)Math.Abs(exact)));
            var places = Math.Clamp(significantDigits - 1 - exponent, 0, 28);
            exact = Math.Round(exact, places, MidpointRounding.ToEven);
        }

        return exact.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private static string RenderTimestamp(DateTime value, int fractionalDigits)
    {
        var fraction = fractionalDigits > 0
            ? "." + new string('f', fractionalDigits)
            : string.Empty;

        return value.ToString("yyyy-MM-dd'T'HH:mm:ss" + fraction, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A string in quotes, with the four characters escaped that would otherwise make one
    /// field look like two, or one row like two.
    /// </summary>
    private static string RenderString(string value)
    {
        var text = new System.Text.StringBuilder(value.Length + 2);
        text.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"': text.Append("\\\""); break;
                case '\\': text.Append("\\\\"); break;
                case '\t': text.Append("\\t"); break;
                case '\n': text.Append("\\n"); break;
                case '\r': text.Append("\\r"); break;
                default: text.Append(character); break;
            }
        }

        text.Append('"');
        return text.ToString();
    }
}
