namespace Model.AbstractRepresentation.Enums;

public enum RelationRole
{
    Owning = 1,   // strana s fyzickým cizím klíčem
    Inverse = 2,  // strana s navigační kolekcí/referencí bez sloupce
}