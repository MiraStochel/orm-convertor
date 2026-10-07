using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace LinqBuilding;

/// <summary>
/// How a column of an entity is reached in the LINQ a target writes (decision 118). The two
/// LINQ targets differ here and nowhere else in the member they name: EF Core's entity
/// declares a scalar property for a foreign key column no property of the source maps
/// (decision 012) and the query names it, NHibernate's reaches the referenced key through
/// the owning reference as HQL does (<see cref="AbstractWrappers.ColumnMember"/>). One
/// answer per target, asked by the builder and its visitor alike, so that the member the
/// query names is the member the entity declares.
/// </summary>
public interface ILinqMembers
{
    /// <summary>The member path the column is written as, relative to the alias of its entity; null where nothing maps the column.</summary>
    string? PathOf(EntityMap? map, string column);

    /// <summary>The language type of the member <see cref="PathOf"/> writes; null where nothing maps the column.</summary>
    LangType? TypeOf(EntityMap? map, string column);

    /// <summary>
    /// The value scalar of the member <see cref="PathOf"/> writes and whether the entity
    /// declares it nullable - a part of the key never, whatever its language type says. Null
    /// for a member of a reference type, or one nothing types. What a composite key of a join
    /// needs: the two anonymous types are one type only where their members agree in type.
    /// </summary>
    (ScalarType Scalar, bool Nullable)? DeclaredValue(EntityMap? map, string column);
}

/// <summary>
/// The members as a language over entities names them (<see cref="AbstractWrappers.ColumnMember"/>):
/// the scalar property that maps the column, or the referenced key through the owning
/// reference that holds a foreign key column no property maps. What NHibernate's LINQ names,
/// because its provider resolves the path the way its HQL does.
/// </summary>
public sealed class EntityPathMembers : ILinqMembers
{
    public static EntityPathMembers Instance { get; } = new();

    public string? PathOf(EntityMap? map, string column) => AbstractWrappers.ColumnMember.PathOf(map, column);

    public LangType? TypeOf(EntityMap? map, string column) => AbstractWrappers.ColumnMember.TypedBy(map, column)?.Property.Type;

    public (ScalarType Scalar, bool Nullable)? DeclaredValue(EntityMap? map, string column)
    {
        if (map is null)
        {
            return null;
        }

        if (AbstractWrappers.ColumnMember.ScalarOf(map, column) is { } scalar)
        {
            var keyPart = map.PrimaryKey?.Parts.Any(part => ReferenceEquals(part.PropertyMap, scalar)
                || string.Equals(part.PropertyMap.ColumnName ?? part.PropertyMap.Property.Name, column, StringComparison.OrdinalIgnoreCase)) == true;
            return ValueOf(scalar.Property.Type, nullable: !keyPart && scalar.Property.Type?.IsNullable == true);
        }

        // The referenced key reached through the reference: the key itself is never nullable.
        return ValueOf(AbstractWrappers.ColumnMember.TypedBy(map, column)?.Property.Type, nullable: false);
    }

    /// <summary>A scalar of a value type with the nullability given; null for text, bytes and anything untyped.</summary>
    public static (ScalarType Scalar, bool Nullable)? ValueOf(LangType? type, bool nullable)
        => type?.ScalarType is { } scalar and not (ScalarType.String or ScalarType.Object or ScalarType.ByteArray)
            ? (scalar, nullable)
            : null;
}
