using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// The whole column image of one table as the catalog states it (decision 015): columns,
/// primary key and foreign keys are always read together, because the demand of the target
/// decides what gets written into the model, not what gets asked - and conflict reporting
/// needs the facts the source already carries as well.
/// </summary>
public sealed class TableImage
{
    public required string Schema { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<ColumnImage> Columns { get; init; }

    /// <summary>Primary key columns in key order; empty when the table has no primary key.</summary>
    public required IReadOnlyList<string> PrimaryKeyColumns { get; init; }

    public required IReadOnlyList<ForeignKeyImage> ForeignKeys { get; init; }

    /// <summary>
    /// Unique constraints other than the primary key, which has its own member above
    /// (decision 055). Empty for most tables, which is why - like
    /// <see cref="ColumnImage.IsRowVersion"/> - it is not required.
    /// </summary>
    public IReadOnlyList<UniqueConstraintImage> UniqueConstraints { get; init; } = [];

    public string QualifiedName => $"{Schema}.{Name}";

    /// <summary>
    /// Finds a column by name. Case-insensitive, matching the default collation of the
    /// catalog the image came from.
    /// </summary>
    public ColumnImage? FindColumn(string name)
        => Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One column of a table image. <see cref="Type"/> is null when the SQL type has no
/// family in <see cref="DatabaseType"/> - the fact is then simply not supplied.
/// Length, precision and scale carry only the values meaningful for the type: length for
/// character and binary columns (null for MAX), precision and scale for decimals, and the
/// fractional-second precision for date-time columns. <see cref="IsUnicode"/> is the
/// unicode facet of character columns, and <see cref="SourceSqlType"/> keeps the
/// catalog's literal spelling where the family is coarser than the type - money,
/// datetime, rowversion (decision 019).
/// </summary>
public sealed class ColumnImage
{
    public required string Name { get; init; }

    public DatabaseType? Type { get; init; }

    public bool? IsUnicode { get; init; }

    public string? SourceSqlType { get; init; }

    public int? Length { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public required bool IsNullable { get; init; }

    public required bool IsIdentity { get; init; }

    /// <summary>
    /// Whether the column is a rowversion - a claim of its own beside the type, because
    /// the type family only says binary while the schema says the column carries the row
    /// version (decisions 019 and 030). Defaults to false: most columns are not one.
    /// </summary>
    public bool IsRowVersion { get; init; }
}

/// <summary>
/// One foreign key of a table image. Column pairs are kept in the order the constraint
/// declares them; a consumer pairing them with a key orders them by that key itself.
/// </summary>
public sealed class ForeignKeyImage
{
    public required string Name { get; init; }

    public required string ReferencedSchema { get; init; }

    public required string ReferencedTable { get; init; }

    public required IReadOnlyList<ForeignKeyColumn> Columns { get; init; }
}

/// <summary>One column pair of a foreign key: the referencing column and the key column it points at.</summary>
public sealed record ForeignKeyColumn(string Column, string ReferencedColumn);

/// <summary>
/// One unique constraint of a table image (decision 055). Columns keep the order of the
/// constraint's own index, which is the order the catalog states and the only one the
/// image can claim; the name is always present, because a database names every constraint
/// even where the script did not.
/// </summary>
public sealed class UniqueConstraintImage
{
    public required string Name { get; init; }

    public required IReadOnlyList<string> Columns { get; init; }
}
