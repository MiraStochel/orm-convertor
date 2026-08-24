using AbstractWrappers.Descriptors;
using Model;

namespace NHibernateWrappers;

/// <summary>
/// NHibernate places four requirements on a persistent class. Two of them — the implicit
/// parameterless constructor and the class not being sealed — are satisfied today only
/// because no builder emits a constructor or the sealed keyword. Stating them as forbidden
/// markers turns an accident into a checked property.
/// </summary>
public static class NHibernateDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.NHibernate,

        // Pinned by decision 013; the canonical table is in docs/architecture.md and a
        // test binds this value to the package the verification level loads. The release
        // decides which syntax the artifact means - the emitted type names are verified
        // against the TypeFactory registry of exactly this version (decision 019).
        Version = "5.7.0",

        EnforcedMembers =
        [
            new EnforcedMember
            {
                Name = "virtual on mapped members",
                Condition = EnforcedMemberCondition.Always,
                Marker = "virtual ",
                Reason = "Lazy loading is implemented by subclassing the entity at runtime, "
                       + "so every mapped member has to be overridable.",
            },
            new EnforcedMember
            {
                Name = "non-sealed persistent class",
                Condition = EnforcedMemberCondition.Always,
                ForbiddenMarker = "sealed class",
                Reason = "A proxy is a subclass of the entity, so a sealed class cannot be "
                       + "proxied at all.",
            },
            new EnforcedMember
            {
                Name = "parameterless constructor",
                Condition = EnforcedMemberCondition.Always,
                ForbiddenMarker = "public {ClassName}(",
                Reason = "NHibernate instantiates entities without arguments. Declaring any "
                       + "constructor removes the implicit parameterless one, so the artifact "
                       + "must declare none.",
            },
            // Two markers for one requirement, because a concrete declaration can appear
            // under either name. Forbidden rather than required: a required "IList<" would
            // be satisfied by the first collection, while a forbidden concrete declaration
            // is caught wherever it appears (decision 035). The initializer keeps the
            // concrete type - "new List<T>()" - which is why the markers anchor on virtual.
            new EnforcedMember
            {
                Name = "collection declared by IList, not List",
                Condition = EnforcedMemberCondition.Always,
                ForbiddenMarker = "virtual List<",
                Reason = "A persistent collection is replaced with NHibernate's own "
                       + "implementation when the entity loads, which cannot be assigned "
                       + "to a concrete List<T>. See decision 035.",
            },
            new EnforcedMember
            {
                Name = "collection declared by ISet, not HashSet",
                Condition = EnforcedMemberCondition.Always,
                ForbiddenMarker = "virtual HashSet<",
                Reason = "A persistent collection is replaced with NHibernate's own "
                       + "implementation when the entity loads, which cannot be assigned "
                       + "to a concrete HashSet<T>. See decision 035.",
            },
            new EnforcedMember
            {
                Name = "[Serializable] on a composite-id class",
                Condition = EnforcedMemberCondition.CompositePrimaryKey,
                Marker = "[Serializable]",
                Reason = "A composite identifier is used as a dictionary key inside the "
                       + "session and has to be serializable. See decision 006.",
            },
            new EnforcedMember
            {
                Name = "Equals override for a composite key",
                Condition = EnforcedMemberCondition.CompositePrimaryKey,
                Marker = "public override bool Equals(object? obj)",
                Reason = "Without it the session factory fails to build with "
                       + "\"composite-id class must override Equals()\". See decision 006.",
            },
            new EnforcedMember
            {
                Name = "GetHashCode override for a composite key",
                Condition = EnforcedMemberCondition.CompositePrimaryKey,
                Marker = "public override int GetHashCode()",
                Reason = "Identity of a composite key is decided by value, so hashing has to "
                       + "follow the same parts as Equals. See decision 006.",
            },
        ],

        Support = new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.Expressible,      // <class table="…">
            [MappingFactCategory.SchemaName] = FactSupport.Expressible,     // <class schema="…">
            [MappingFactCategory.ColumnName] = FactSupport.Expressible,     // column attribute
            [MappingFactCategory.DatabaseType] = FactSupport.Expressible,   // type attribute
            [MappingFactCategory.Length] = FactSupport.Expressible,         // length attribute
            [MappingFactCategory.PrecisionAndScale] = FactSupport.Expressible,
            [MappingFactCategory.Nullability] = FactSupport.Expressible,    // not-null attribute

            // A <class> element has to carry <id> or <composite-id>; a mapping without an
            // identifier is not accepted. This is the category where NHibernate differs
            // from EF Core, which falls back to a keyless type.
            [MappingFactCategory.PrimaryKey] = FactSupport.Required,

            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.Expressible, // <generator>
            [MappingFactCategory.ForeignKeyColumns] = FactSupport.Expressible,
            [MappingFactCategory.VersionColumn] = FactSupport.Expressible,   // <version> element

            // unique="true" for one column, unique-key="…" to group several (decision 055).
            // Both live on <property>, so a constraint over a key part or a navigation is
            // the narrowing the builder reports at the point of emission.
            [MappingFactCategory.UniqueConstraint] = FactSupport.Expressible,
        },

        // HQL covers every category except set operations: NHibernate 5.7.0 has no UNION,
        // INTERSECT or EXCEPT in HQL. Pagination is expressible even though it is not part of
        // the HQL text - it is SetMaxResults on the surrounding IQuery.
        QuerySupport = new Dictionary<QueryFeature, FactSupport>
        {
            [QueryFeature.Projection] = FactSupport.Expressible,
            [QueryFeature.Filtering] = FactSupport.Expressible,
            [QueryFeature.Join] = FactSupport.Expressible,
            [QueryFeature.JoinKind] = FactSupport.Expressible,
            [QueryFeature.Aggregation] = FactSupport.Expressible,
            [QueryFeature.Grouping] = FactSupport.Expressible,
            [QueryFeature.PostAggregationFiltering] = FactSupport.Expressible,
            [QueryFeature.Ordering] = FactSupport.Expressible,
            [QueryFeature.Pagination] = FactSupport.Expressible,
            [QueryFeature.Subquery] = FactSupport.Expressible,
            [QueryFeature.SetOperation] = FactSupport.NotExpressible,
            [QueryFeature.QueryParameter] = FactSupport.Expressible,
        },
    };
}