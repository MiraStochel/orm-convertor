namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// How the value of a key part comes to exist. The values name mechanisms shared by several
/// frameworks, not generators of one of them; what a source expresses beyond them is kept on
/// PrimaryKeyPart as SourceStrategyName and SourceStrategyParameters (decisions 011 and 020).
/// </summary>
public enum PrimaryKeyStrategy
{
    /// <summary>Nobody stated how the value arises.</summary>
    Unspecified = 0,

    /// <summary>The application supplies the value before the insert.</summary>
    Assigned = 1,

    /// <summary>The framework picks the mechanism for the dialect in use.</summary>
    Auto = 2,

    /// <summary>Auto-increment column; the database produces the value on insert.</summary>
    Identity = 3,

    /// <summary>Value taken from a database sequence.</summary>
    Sequence = 4,

    /// <summary>Values allocated in blocks from a counter kept outside the entity table.</summary>
    HiLo = 5,

    /// <summary>Globally unique value generated outside the database.</summary>
    Uuid = 6,

    /// <summary>Counter held in the memory of a single process; NHibernate only.</summary>
    Increment = 7,
}