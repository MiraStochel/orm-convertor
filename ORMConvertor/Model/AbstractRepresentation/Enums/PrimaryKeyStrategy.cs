namespace Model.AbstractRepresentation.Enums;

public enum PrimaryKeyStrategy
{
    None = 0,
    Increment = 1,
    Identity = 2,
    Sequence = 3,
    HiLo = 4,
    Uuid = 5,
    Guid = 6,
}
