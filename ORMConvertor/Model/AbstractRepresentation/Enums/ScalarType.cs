namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Closed list of scalar types the model can name across ecosystems (decision 014).
/// Closed on purpose: only over a finite set can a target descriptor state what the
/// framework is able to express. Unsigned integers are deliberately absent - Java has
/// no counterpart, so they are kept more faithfully as an unknown type than widened.
/// Object means the source wrote the root object type, which is a different claim
/// than an unrecognized name.
/// </summary>
public enum ScalarType
{
    Bool = 1,
    Byte = 2,
    Short = 3,
    Int = 4,
    Long = 5,
    Float = 6,
    Double = 7,
    Decimal = 8,
    Char = 9,
    String = 10,
    DateTime = 11,
    Guid = 12,
    Object = 13,
}
