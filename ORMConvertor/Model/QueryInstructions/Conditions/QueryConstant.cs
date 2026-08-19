using Model.AbstractRepresentation.Enums;

namespace Model.QueryInstructions.Conditions;

/// <summary>
/// A literal value inside a query condition, carried undecorated together with the scalar
/// type it belongs to (decision 024).
///
/// "Undecorated" is the whole point: the model holds <c>Foo</c>, never <c>"Foo"</c> or
/// <c>'Foo'</c>, and <c>2000</c>, never <c>2000m</c>. Quoting and numeric suffixes are
/// properties of the target language, so the parser strips them on the way in and each
/// builder adds its own on the way out. Without the type a builder could only guess from
/// the shape of the text, and that guess is wrong in the same three places every time:
/// a number written in quotes, an empty string, and a date.
/// </summary>
public sealed class QueryConstant
{
    private QueryConstant(string text, ScalarType? type)
    {
        Text = text;
        Type = type;
    }

    /// <summary>The value as written, with the source language's decoration removed.</summary>
    public string Text { get; }

    /// <summary>
    /// Scalar type of the value, or null when the source used a literal we could not place
    /// in the vocabulary. Null is a statement, not a failure: the builder emits the text
    /// verbatim and the parser reports the gap, because silence would mean an incomplete
    /// artifact instead of an incomplete claim.
    /// </summary>
    public ScalarType? Type { get; }

    public static QueryConstant Of(string text, ScalarType type) => new(text, type);

    public static QueryConstant Unrecognised(string text) => new(text, null);

    public override string ToString() => Type is null ? Text : $"{Text}:{Type}";
}
