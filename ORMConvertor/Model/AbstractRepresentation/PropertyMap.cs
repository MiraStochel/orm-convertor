using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public class PropertyMap
{
    public required Property Property { get; set; }

    public string? ColumnName { get; set; }

    public DatabaseType? Type { get; set; }

    /// <summary>
    /// Whether the column holds unicode character data - a facet of the type claim, not
    /// part of the family (decision 019). Null means the source did not say and the
    /// target fills in its own convention.
    /// </summary>
    public bool? IsUnicode { get; set; }

    /// <summary>
    /// The literal type as the source spelled it, kept when the family vocabulary does
    /// not capture the type or is coarser than the claim - a record beside the
    /// definition, not part of it (decision 019), the same role SourceStrategyName has
    /// for the key strategy.
    /// </summary>
    public string? SourceSqlType { get; set; }

    public int? Precision { get; set; }

    public int? Scale { get; set; }

    public int? Length { get; set; }

    public bool? IsNullable { get; set; }

    public Dictionary<string, string> OtherDatabaseProperties { get; set; } = [];
}
