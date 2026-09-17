using AbstractWrappers.Descriptors;

namespace JakartaPersistence;

/// <summary>
/// What Jakarta Persistence 3.2 itself requires and can express, declared once for both
/// implementations (decisions 076 and 077): the enforced members of the specification and
/// the support table of the annotation subset. Each wrapper's descriptor takes these and
/// adds what is its own.
/// </summary>
public static class JakartaPersistenceDescriptor
{
    public const string SpecificationLevel = "3.2";

    /// <summary>
    /// The members the specification forces onto an entity (§2.1) and the key class it
    /// demands for a composite key (§2.4): all of them either a forbidden spelling - the
    /// class must not be final, its persistent fields must not be final, and it must keep
    /// a no-arg constructor, so the artifact must declare none - or the key class's
    /// required parts (decision 006).
    /// </summary>
    public static IReadOnlyList<EnforcedMember> EnforcedMembers { get; } =
    [
        new EnforcedMember
        {
            Name = "non-final entity class",
            Condition = EnforcedMemberCondition.Always,
            ForbiddenMarker = "final class",
            Reason = "The entity class must not be final (Jakarta Persistence 3.2 §2.1): a proxy is a "
                   + "subclass of the entity, and Hibernate gives up lazy loading on a final class.",
        },
        new EnforcedMember
        {
            Name = "non-final persistent fields",
            Condition = EnforcedMemberCondition.Always,
            ForbiddenMarker = "private final ",
            Reason = "No persistent field or method may be final (Jakarta Persistence 3.2 §2.1); a "
                   + "final field could not be set by the provider.",
        },
        new EnforcedMember
        {
            Name = "no-arg constructor",
            Condition = EnforcedMemberCondition.Always,
            ForbiddenMarker = "public {ClassName}(",
            Reason = "The entity needs a public or protected no-arg constructor (§2.1). Declaring "
                   + "any constructor removes the implicit one, so the artifact must declare none.",
        },
        new EnforcedMember
        {
            Name = "@IdClass naming the key class of a composite key",
            Condition = EnforcedMemberCondition.CompositePrimaryKey,
            Marker = "@IdClass(",
            Reason = "A composite key needs a key class even when the key attributes stay on the "
                   + "entity (§2.4); the flat rendering of decision 006 names it through @IdClass.",
        },
        new EnforcedMember
        {
            Name = "serializable key class",
            Condition = EnforcedMemberCondition.CompositePrimaryKey,
            Marker = "implements Serializable",
            Reason = "The key class must be serializable (§2.4); the provider uses it as the "
                   + "identifier value of the entity.",
        },
        new EnforcedMember
        {
            Name = "equals override on the key class",
            Condition = EnforcedMemberCondition.CompositePrimaryKey,
            Marker = "public boolean equals(Object",
            Reason = "The key class must define equals (§2.4); identity of a composite key is "
                   + "decided by value. See decision 006.",
        },
        new EnforcedMember
        {
            Name = "hashCode override on the key class",
            Condition = EnforcedMemberCondition.CompositePrimaryKey,
            Marker = "public int hashCode()",
            Reason = "The key class must define hashCode consistently with equals (§2.4). See decision 006.",
        },
    ];

    /// <summary>
    /// The annotation subset of decision 077 records every category of the model; the
    /// identifier is required, because an entity without @Id is refused by both
    /// implementations at bootstrap (§2.4 - "every entity must have a primary key").
    /// </summary>
    public static IReadOnlyDictionary<MappingFactCategory, FactSupport> Support { get; } =
        new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.Expressible,          // @Table(name)
            [MappingFactCategory.SchemaName] = FactSupport.Expressible,         // @Table(schema)
            [MappingFactCategory.ColumnName] = FactSupport.Expressible,         // @Column(name)
            [MappingFactCategory.DatabaseType] = FactSupport.Expressible,       // columnDefinition, @Nationalized
            [MappingFactCategory.Length] = FactSupport.Expressible,             // @Column(length)
            [MappingFactCategory.PrecisionAndScale] = FactSupport.Expressible,  // @Column(precision, scale)
            [MappingFactCategory.Nullability] = FactSupport.Expressible,        // @Column(nullable)
            [MappingFactCategory.PrimaryKey] = FactSupport.Required,            // @Id
            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.Expressible, // @GeneratedValue
            [MappingFactCategory.ForeignKeyColumns] = FactSupport.Expressible,  // @JoinColumn
            [MappingFactCategory.VersionColumn] = FactSupport.Expressible,      // @Version
            [MappingFactCategory.UniqueConstraint] = FactSupport.Expressible,   // @UniqueConstraint
            [MappingFactCategory.TransientProperty] = FactSupport.Expressible,  // @Transient
        };

    /// <summary>
    /// JPQL 3.2 with the entity join both implementations add covers every category; the
    /// pagination lives on the query object, outside the text, like NHibernate's.
    /// </summary>
    public static IReadOnlyDictionary<QueryFeature, FactSupport> QuerySupport { get; } =
        new Dictionary<QueryFeature, FactSupport>
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
            [QueryFeature.SetOperation] = FactSupport.Expressible,
            [QueryFeature.QueryParameter] = FactSupport.Expressible,
        };
}
