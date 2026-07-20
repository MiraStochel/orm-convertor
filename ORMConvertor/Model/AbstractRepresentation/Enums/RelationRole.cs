namespace Model.AbstractRepresentation.Enums;

public enum RelationRole
{
    Owning = 1,   // side holding the physical foreign key
    Inverse = 2,  // side with a navigation collection/reference without a column
}