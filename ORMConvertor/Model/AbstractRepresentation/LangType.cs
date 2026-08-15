using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

/// <summary>
/// Ecosystem-neutral language type of a property (decision 014, after JSS §5.2).
/// Four categories: a scalar from the closed <see cref="Enums.ScalarType"/> list, a
/// reference to another entity by name, a collection with a recursive element type,
/// and an unknown type kept under the name the source wrote. Instances are created
/// through the factory methods only, so an invalid combination of the per-category
/// facts cannot be written down.
/// </summary>
public sealed class LangType
{
    public LangTypeCategory Category { get; }

    /// <summary>Scalar only: the value from the closed list.</summary>
    public ScalarType? ScalarType { get; }

    /// <summary>Reference only: name of the target entity, never an object reference (decision 001).</summary>
    public string? TargetEntity { get; }

    /// <summary>Collection only: type of the element, itself a full <see cref="LangType"/>.</summary>
    public LangType? ElementType { get; }

    /// <summary>Collection only: kind of the collection.</summary>
    public CollectionKind? CollectionKind { get; }

    /// <summary>Unknown only: the type name exactly as the source wrote it.</summary>
    public string? SourceName { get; }

    /// <summary>
    /// Language-side nullability, next to the database-side one on PropertyMap, so that
    /// rule E4 (¬NullableLang ⇒ ¬NullableDB) has both sides to compare. Living on the
    /// type rather than the property, it also covers the element of a collection.
    /// </summary>
    public bool IsNullable { get; }

    private LangType(
        LangTypeCategory category,
        ScalarType? scalarType = null,
        string? targetEntity = null,
        LangType? elementType = null,
        CollectionKind? collectionKind = null,
        string? sourceName = null,
        bool isNullable = false)
    {
        Category = category;
        ScalarType = scalarType;
        TargetEntity = targetEntity;
        ElementType = elementType;
        CollectionKind = collectionKind;
        SourceName = sourceName;
        IsNullable = isNullable;
    }

    public static LangType Scalar(ScalarType scalarType, bool isNullable = false)
        => new(LangTypeCategory.Scalar, scalarType: scalarType, isNullable: isNullable);

    public static LangType Reference(string targetEntity, bool isNullable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntity);

        return new(LangTypeCategory.Reference, targetEntity: targetEntity, isNullable: isNullable);
    }

    public static LangType Collection(
        LangType elementType,
        Enums.CollectionKind kind = Enums.CollectionKind.Unspecified,
        bool isNullable = false)
    {
        ArgumentNullException.ThrowIfNull(elementType);

        return new(LangTypeCategory.Collection, elementType: elementType, collectionKind: kind, isNullable: isNullable);
    }

    public static LangType Unknown(string sourceName, bool isNullable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        return new(LangTypeCategory.Unknown, sourceName: sourceName, isNullable: isNullable);
    }
}
