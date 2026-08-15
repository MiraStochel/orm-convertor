namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Category of a language type (decision 014, after JSS §5.2). Scalar, Reference and
/// Collection follow the paper; Unknown is the escape for a name the source wrote and
/// the model cannot place, which must never surface as an exception.
/// </summary>
public enum LangTypeCategory
{
    Scalar = 1,
    Reference = 2,
    Collection = 3,
    Unknown = 4,
}
