using Model.AbstractRepresentation.Enums;

namespace Common.Sql;

/// <summary>
/// One reading of a literal SQL type in the neutral vocabulary (decision 019): the family,
/// the facets the name and its arguments claim, and whether the literal spelling belongs on
/// the escape path because the family is coarser than the name (money, datetime) or missing
/// altogether. A null <see cref="Type"/> means the vocabulary does not capture the name.
///
/// One type for every reader of a literal type: the EF Core [Column(TypeName)], the JPA
/// columnDefinition and the sql-type of an hbm.xml column all used to have a copy of this
/// (decision 086).
/// </summary>
public readonly record struct SqlTypeReading(
    DatabaseType? Type,
    bool? IsUnicode = null,
    int? Length = null,
    int? Precision = null,
    int? Scale = null,
    bool KeepLiteral = false);
