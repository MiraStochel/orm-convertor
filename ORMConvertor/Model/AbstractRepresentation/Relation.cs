using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public sealed class Relation
{
    public string? Name { get; set; }                  // rozlišení víc vztahů mezi stejnou dvojicí entit
    public required Cardinality Cardinality { get; set; }
    public required RelationRole Role { get; set; }
    public required string SourceEntity { get; set; }  // název entity; string dle rozhodnutí 7.1
    public required string TargetEntity { get; set; }

    /// <summary>Páry sloupců FK; prázdné = sloupce zatím nejsou rozresolvované (viz F4/F5).</summary>
    public IReadOnlyList<ColumnPair> ColumnPairs { get; set; } = [];

    /// <summary>Název navigační vlastnosti na zdrojové entitě, pokud existuje.</summary>
    public string? SourceNavigationProperty { get; set; }

    public bool IsUnique { get; set; }                 // true = 1:1 (unikátní omezení na FK sloupcích)
    public string? InverseRelationName { get; set; }   // párování Owning <-> Inverse
}