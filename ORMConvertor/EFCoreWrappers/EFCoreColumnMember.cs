using AbstractWrappers;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// The member a column is written as in the LINQ the EF Core target emits. A foreign key
/// column no property of the source maps gets a scalar property of its own from the entity
/// builder - [ForeignKey] names properties of the class, not columns, so the builder
/// declares one per pair, named by the navigation and the key part it points at (decision
/// 012) -, and the query names that property, because EF Core reads it off the foreign key
/// itself, whereas <c>o.customer.id</c>, the form the other languages over entities take
/// (<see cref="ColumnMember"/>), costs a join to the referenced table (measured against
/// 10.0.10 with ToQueryString). One home for the name, so that the property the query names
/// is the property the entity declares.
/// </summary>
internal static class EFCoreColumnMember
{
    /// <summary>The name of the property declared for a pair whose referenced key part is <paramref name="referenced"/>.</summary>
    public static string NameFor(string navigation, PropertyMap referenced) => navigation + referenced.Property.Name;

    /// <summary>
    /// The member path the column is written as: its property, the declared foreign key
    /// property, or - where the entity builder declares none - the referenced key through
    /// the navigation, which returns the same rows at the cost of a join. Null where nothing
    /// maps the column.
    /// </summary>
    public static string? PathOf(EntityMap? map, string column)
        => Declared(map, column) is { } declared
            ? declared.Name
            : ColumnMember.PathOf(map, column);

    /// <summary>The language type of the member <see cref="PathOf"/> writes; null where nothing maps the column.</summary>
    public static LangType? TypeOf(EntityMap? map, string column)
        => Declared(map, column) is { } declared
            ? declared.Type
            : ColumnMember.TypedBy(map, column)?.Property.Type;

    /// <summary>
    /// The value scalar of the member <see cref="PathOf"/> writes and whether the entity
    /// declares it nullable - a part of the key never, whatever its language type says, a
    /// declared foreign key property as its navigation is, any other property as its language
    /// type is, and the referenced key reached through a navigation never. Null for a member
    /// of a reference type, or one nothing types. What a composite key of a join needs: the
    /// two anonymous types are one type only where their members agree in type, and a
    /// nullable foreign key beside the key it points at is the usual case.
    /// </summary>
    public static (ScalarType Scalar, bool Nullable)? DeclaredValue(EntityMap? map, string column)
    {
        if (map is null)
        {
            return null;
        }

        if (ColumnMember.ScalarOf(map, column) is { } scalar)
        {
            var keyPart = map.PrimaryKey?.Parts.Any(part => ReferenceEquals(part.PropertyMap, scalar)
                || string.Equals(part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name, column, StringComparison.OrdinalIgnoreCase)) == true;
            return ValueOf(scalar.Property.Type, nullable: !keyPart && scalar.Property.Type?.IsNullable == true);
        }

        if (Declared(map, column) is { } declared)
        {
            return ValueOf(declared.Type, nullable: declared.Type?.IsNullable == true);
        }

        return ValueOf(ColumnMember.TypedBy(map, column)?.Property.Type, nullable: false);
    }

    private static (ScalarType Scalar, bool Nullable)? ValueOf(LangType? type, bool nullable)
        => type?.ScalarType is { } scalar and not (ScalarType.String or ScalarType.Object or ScalarType.ByteArray)
            ? (scalar, nullable)
            : null;

    /// <summary>
    /// The property the entity builder declares, or names, for a foreign key column held by
    /// an owning reference, with its language type - the type of the key part it points at,
    /// nullable as the navigation is, an unstated nullability counting as optional. Null for
    /// a column a scalar property maps, and where the builder declares nothing: the entity has
    /// no navigation property for the reference, or a referenced key part has no language
    /// type, so no [ForeignKey] is written. The conditions are the entity builder's own.
    /// </summary>
    private static (string Name, LangType? Type)? Declared(EntityMap? map, string column)
    {
        if (map is null
            || ColumnMember.ScalarOf(map, column) is not null
            || ColumnMember.HeldBy(map, column) is not { } held
            || held.Reference.SourceNavigationProperty is not { } navigation
            || map.PropertyMaps.FirstOrDefault(pm => pm.Property.Name == navigation) is not { } navigationMap
            || held.Reference.ColumnPairs.Any(p => p.Target.Property.Type is null))
        {
            return null;
        }

        var existing = map.PropertyMaps.FirstOrDefault(pm =>
            pm.Property.Name == held.Pair.Source.Property.Name && pm.Property.Type is not null);
        if (existing is not null)
        {
            return (existing.Property.Name, existing.Property.Type);
        }

        var referenced = held.Pair.Target.Property.Type!;
        var type = referenced.ScalarType is { } scalar
            ? LangType.Scalar(scalar, navigationMap.IsNullable ?? true)
            : referenced;

        return (NameFor(navigation, held.Pair.Target), type);
    }
}
