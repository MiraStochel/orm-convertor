using System.Globalization;
using System.Text.Json;
using SampleData;
using Tests.Differential;

namespace Tests.LdbcJudge;

/// <summary>
/// The validation set of LDBC Interactive v1 and the rows of a generated artifact, put into the
/// one canonical form of decision 089 so that they meet (decision 117). The rendering is
/// <see cref="ResultRow"/>'s, with its default settings - the results of the workload have no
/// decimal or approximate number but the weight of a path, which the defaults state exactly -;
/// what is added here is how a field of the driver's JSON becomes a value, by the kind the
/// binding of the catalog names:
/// <list type="bullet">
/// <item>a moment is milliseconds since the epoch in UTC, a date the milliseconds of its
/// midnight, a flag a truth value the text writes as 1 or 0;</item>
/// <item>a set is a list both sides are compared as the set of: the elements are joined with
/// the separator in ordinal order, an element that is an object as the values of the named
/// fields joined with the element separator; an empty list is NULL, as the STRING_AGG of the
/// text over no rows is;</item>
/// <item>a result that is one object is a result of one row.</item>
/// </list>
/// The counterpart is <c>LdbcCanonicalForm.java</c>, and that the two agree is held the way
/// the renderers are held: <c>renderer-conformance.txt</c> carries rows converted from the same
/// JSON by both suites. A JSON value the kind does not say how to read throws, for the reason
/// the renderer throws on a type it has no rule for.
/// </summary>
internal static class LdbcCanonicalForm
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private const long MillisecondsPerDay = 86_400_000;

    private static readonly ResultRow.RenderSettings Settings = new();

    /// <summary>The expected result of a read, as the canonical rows of the binding's columns.</summary>
    public static List<string> Expected(LdbcValidation binding, string result)
    {
        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;

        IEnumerable<JsonElement> rows = root.ValueKind switch
        {
            JsonValueKind.Object => [root],
            JsonValueKind.Array => root.EnumerateArray(),
            _ => throw new NotSupportedException(
                $"{binding.Operation}: the expected result is a JSON {root.ValueKind}, neither a row nor a list of rows."),
        };

        var rendered = rows.Select(row => ResultRow.Render(
            [.. binding.Fields.Select(field => Expected(field, Member(binding, row, field.Field)))],
            Settings));

        return binding.Ordered ? [.. rendered] : ResultRow.Sorted(rendered);
    }

    /// <summary>The rows a generated artifact returned, with each column read as the binding's kind says.</summary>
    public static List<string> Actual(LdbcValidation binding, IEnumerable<object?[]> rows)
    {
        var rendered = rows.Select(row => ResultRow.Render(
            [.. binding.Fields.Select((field, index) => Actual(field, row[index]))],
            Settings));

        return binding.Ordered ? [.. rendered] : ResultRow.Sorted(rendered);
    }

    /// <summary>One field of the expected result as the value the renderer states.</summary>
    public static object? Expected(LdbcResultField field, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return field.Kind switch
        {
            LdbcValueKind.Value => Scalar(field, value),
            LdbcValueKind.Moment => Moment(Integer(field, value)),
            LdbcValueKind.Date => Date(field, Integer(field, value)),
            LdbcValueKind.Flag => value.ValueKind switch
            {
                JsonValueKind.True => 1L,
                JsonValueKind.False => 0L,
                _ => throw Unreadable(field, value),
            },
            LdbcValueKind.Set => List(field, value, ordered: false),
            LdbcValueKind.Sequence => List(field, value, ordered: true),
            _ => throw new NotSupportedException($"{field.Column}: the kind {field.Kind} has no rule (decision 117)."),
        };
    }

    /// <summary>
    /// One column of a row a generated artifact returned, read as the binding's kind says: a
    /// date that a framework hands back as a moment at midnight is that date, a truth value is
    /// the number the text writes for it, and a joined list is the set of its elements. The
    /// values are compared, not the types (decision 089), and a framework that maps a DATE
    /// column to DateTime and one that maps it to DateOnly return the same value.
    /// </summary>
    public static object? Actual(LdbcResultField field, object? value) => (field.Kind, value) switch
    {
        (_, null or DBNull) => null,
        (LdbcValueKind.Date, DateTime moment) when moment.TimeOfDay == TimeSpan.Zero => DateOnly.FromDateTime(moment),
        (LdbcValueKind.Flag, bool flag) => flag ? 1L : 0L,
        (LdbcValueKind.Set, string joined) => string.Join(
            field.Separator, joined.Split(field.Separator).OrderBy(element => element, StringComparer.Ordinal)),
        _ => value,
    };

    /// <summary>
    /// The arguments of a read, in the order of the parameters of the generated method: each
    /// made from the fields of the operation as the binding says and typed as the parameter of
    /// the text declares it - a number, a text, or a moment from the milliseconds the driver writes.
    /// </summary>
    public static object?[] Arguments(LdbcQuery query, IEnumerable<string> parameterNames, string operation)
    {
        var binding = query.Validation!;

        using var document = JsonDocument.Parse(operation);
        var root = document.RootElement;

        return [.. parameterNames.Select(name =>
        {
            var argument = binding.Arguments.SingleOrDefault(candidate => candidate.Parameter == name)
                ?? throw new InvalidOperationException(
                    $"{query.Key}: the generated method takes the parameter {name}, which the binding of {binding.Operation} does not bind.");
            var parameter = query.Parameters.Single(candidate => candidate.Name == name);

            return Argument(query.Key, argument, parameter.SqlType, root);
        })];
    }

    private static object Argument(string key, LdbcArgument argument, string sqlType, JsonElement operation)
    {
        var field = Field(key, operation, argument.Field);

        object value = argument.Derivation switch
        {
            LdbcDerivation.Field => field.ValueKind == JsonValueKind.String ? field.GetString()! : field.GetInt64(),
            LdbcDerivation.PlusDays => field.GetInt64() + Field(key, operation, argument.Operand!).GetInt64() * MillisecondsPerDay,
            LdbcDerivation.NextMonth => field.GetInt64() % 12 + 1,
            _ => throw new NotSupportedException($"{key}: the derivation {argument.Derivation} has no rule (decision 117)."),
        };

        return sqlType.Split('(')[0].ToUpperInvariant() switch
        {
            "BIGINT" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            "INT" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "NVARCHAR" or "VARCHAR" => (string)value,
            "DATE" or "DATETIME2" => Moment(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            _ => throw new NotSupportedException($"{key}: no binding for a parameter of type {sqlType} (decision 117)."),
        };
    }

    private static JsonElement Field(string key, JsonElement operation, string name)
        => operation.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidOperationException($"{key}: the operation has no field \"{name}\": {operation.GetRawText()}");

    private static JsonElement Member(LdbcValidation binding, JsonElement row, string name)
        => row.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidOperationException(
                $"{binding.Operation}: a row of the expected result has no field \"{name}\": {row.GetRawText()}");

    private static object Scalar(LdbcResultField field, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number when IsInteger(value) => value.GetInt64(),
        JsonValueKind.Number => value.GetDouble(),
        _ => throw Unreadable(field, value),
    };

    private static long Integer(LdbcResultField field, JsonElement value)
        => value.ValueKind == JsonValueKind.Number && IsInteger(value) ? value.GetInt64() : throw Unreadable(field, value);

    /// <summary>A number is an integer by how JSON writes it, which is how both parsers see it.</summary>
    private static bool IsInteger(JsonElement value) => value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0;

    private static DateTime Moment(long milliseconds) => Epoch.AddTicks(milliseconds * TimeSpan.TicksPerMillisecond);

    private static DateOnly Date(LdbcResultField field, long milliseconds)
    {
        if (milliseconds % MillisecondsPerDay != 0)
        {
            throw new NotSupportedException(
                $"{field.Column}: {milliseconds} is not the midnight of a day, so it is not a date (decision 117).");
        }

        return DateOnly.FromDateTime(Moment(milliseconds));
    }

    private static string? List(LdbcResultField field, JsonElement value, bool ordered)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Unreadable(field, value);
        }

        var elements = value.EnumerateArray().Select(element => Element(field, element)).ToList();

        if (elements.Count == 0)
        {
            return null;
        }

        return string.Join(field.Separator, ordered ? elements : elements.OrderBy(element => element, StringComparer.Ordinal));
    }

    private static string Element(LdbcResultField field, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number when IsInteger(element) => element.GetInt64().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.Object when field.Elements is { } names => string.Join(
            field.ElementSeparator, names.Select(name => element.TryGetProperty(name, out var part)
                ? Element(field with { Elements = null }, part)
                : throw new InvalidOperationException($"{field.Column}: an element has no field \"{name}\": {element.GetRawText()}"))),
        _ => throw Unreadable(field, element),
    };

    private static NotSupportedException Unreadable(LdbcResultField field, JsonElement value)
        => new($"{field.Column}: the kind {field.Kind} has no rule for the JSON {value.ValueKind} {value.GetRawText()} (decision 117).");
}
