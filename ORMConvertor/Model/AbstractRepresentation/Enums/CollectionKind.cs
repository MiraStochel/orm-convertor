namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Kind of a collection type. The distinction is semantic - a set excludes duplicates,
/// a list carries order - so losing it would change behavior, not just notation.
/// Maps stay out of scope because they need a key type as well (decision 014).
/// </summary>
public enum CollectionKind
{
    Unspecified = 0,
    List = 1,
    Set = 2,
}
