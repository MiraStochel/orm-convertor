namespace AbstractWrappers.Descriptors;

/// <summary>
/// Categories of mapping facts a target framework can require, express, or fail to express.
/// The granularity is deliberately coarse: one value per fact a catalog lookup can supply
/// and a builder can emit, not one value per model property.
/// </summary>
public enum MappingFactCategory
{
    TableName = 1,
    SchemaName = 2,
    ColumnName = 3,
    DatabaseType = 4,
    Length = 5,
    PrecisionAndScale = 6,
    Nullability = 7,
    PrimaryKey = 8,
    PrimaryKeyStrategy = 9,
    ForeignKeyColumns = 10,

    /// <summary>
    /// The column carries the row version for optimistic concurrency (decision 030) -
    /// [Timestamp] in EF Core, the version element in NHibernate; Dapper has nowhere
    /// to put it.
    /// </summary>
    VersionColumn = 11,

    /// <summary>
    /// A unique constraint over one or more columns (decision 055) - [Index(…, IsUnique =
    /// true)] in EF Core, unique or unique-key in NHibernate; Dapper has nowhere to put it.
    /// The primary key is not one of these: it has its own category, and stating it twice
    /// would report the same fact twice.
    /// </summary>
    UniqueConstraint = 12,
}