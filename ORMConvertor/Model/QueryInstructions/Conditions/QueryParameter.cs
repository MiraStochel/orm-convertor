using Model.AbstractRepresentation.Enums;

namespace Model.QueryInstructions.Conditions;

/// <summary>
/// A value the query names but does not know: the caller supplies it when the query is
/// bound (decision 083). The fifth operand shape, standing beside
/// <see cref="QueryConstant"/> because the two are the same kind of fact told apart by one
/// thing - a constant is a value the query stated, a parameter is a value it left open.
///
/// The name is carried <b>undecorated</b>, exactly as a constant carries its value
/// undecorated: the model holds <c>id</c>, never <c>@id</c>, <c>:id</c> or <c>#{id}</c>.
/// The decoration belongs to the language, so the parser strips it and each builder adds
/// its own - the same division decision 024 made for quotes and numeric suffixes.
///
/// A positional parameter carries its order instead of a name, counted from one: <c>?1</c>
/// says 1, and HQL's bare <c>?</c> takes the order of its occurrence in the text. One or
/// the other, never both and never neither, because the source that wrote <c>?</c> named
/// nothing and inventing a name here would put a fact into the model that is not in the
/// source (decision 028). Where a name has to exist - in the signature of the generated
/// method, and in every target whose language has no positional form - it is made by the
/// builder and recorded as a convention.
///
/// The value itself is never carried: that is a property of the call, not of the query.
/// </summary>
public sealed class QueryParameter
{
    private QueryParameter(string? name, int? position, ScalarType? type, bool isCollection)
    {
        Name = name;
        Position = position;
        Type = type;
        IsCollection = isCollection;
    }

    /// <summary>The name the source gave the parameter, undecorated; null for a positional one.</summary>
    public string? Name { get; }

    /// <summary>The order of a positional parameter, counted from one; null for a named one.</summary>
    public int? Position { get; }

    /// <summary>
    /// Scalar type of the value, or null when nobody has stated it yet. The source states
    /// it only where its language can - MyBatis writes <c>#{id,javaType=Integer}</c> - so
    /// the usual answer is null and the builder template derives it from the other side of
    /// the comparison (decision 083).
    /// </summary>
    public ScalarType? Type { get; }

    /// <summary>
    /// Whether the value is a list rather than a single value: <c>in (:ids)</c>,
    /// <c>ids.Contains(c.Id)</c>. A property of the value, which is why it sits here and
    /// not on <see cref="QueryOperand"/> - the position in the condition is the operand's
    /// business, the shape of the bound value is the parameter's. The scalar of a
    /// collection parameter types its <em>elements</em>, as the values of an IN list type
    /// themselves (decision 074).
    /// </summary>
    public bool IsCollection { get; }

    public bool IsPositional => Position is not null;

    public static QueryParameter Named(string name, ScalarType? type = null, bool isCollection = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new QueryParameter(name, null, type, isCollection);
    }

    public static QueryParameter Positional(int position, ScalarType? type = null, bool isCollection = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        return new QueryParameter(null, position, type, isCollection);
    }

    /// <summary>
    /// The same parameter with its scalar filled in, which is what the builder template's
    /// gate produces once it has derived the type from the comparison (decision 083). A new
    /// instance rather than a setter: the condition tree a parser handed over is not
    /// rewritten, the resolved parameter goes onto the builder's own list.
    /// </summary>
    public QueryParameter WithType(ScalarType type) => new(Name, Position, type, IsCollection);

    public override string ToString()
    {
        var name = Name ?? $"?{Position}";
        var scalar = Type is null ? string.Empty : $":{Type}";
        return IsCollection ? $"{name}[]{scalar}" : $"{name}{scalar}";
    }
}
