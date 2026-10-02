using AbstractWrappers.Descriptors;
using Model;

namespace EFCoreWrappers;

/// <summary>
/// EF Core expresses mapping through data annotations. The one thing it forces onto the
/// artifact is the keyless marker: without it a property named Id or {TypeName}Id becomes
/// the primary key by convention, so an entity the model holds without a key would
/// silently acquire one.
/// </summary>
public static class EFCoreDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.EFCore,
        Ecosystem = Ecosystem.DotNet,

        // Pinned by decision 013; the canonical table is in docs/architecture.md and a
        // test binds this value to the package the verification level loads. The release
        // decides which syntax the artifact means - [PrimaryKey] exists since EF Core 7.
        Version = "10.0.10",

        // The database system these artifacts are written for (decision 086); the
        // only dialect this version targets, declared rather than assumed.
        Dialect = DatabaseDialect.SqlServer2022,

        EnforcedMembers =
        [
            new EnforcedMember
            {
                Name = "keyless marker on an entity without a key",
                Condition = EnforcedMemberCondition.NoPrimaryKey,
                Marker = "[Keyless]",
                Reason = "EF Core derives a primary key by convention from a property named "
                       + "Id or {TypeName}Id. Without [Keyless] an entity the model holds "
                       + "without a key would gain one that nobody stated.",
            },
        ],

        Support = new Dictionary<MappingFactCategory, FactSupport>
        {
            [MappingFactCategory.TableName] = FactSupport.Expressible,      // [Table]
            [MappingFactCategory.SchemaName] = FactSupport.Expressible,     // [Table(Schema = …)]
            [MappingFactCategory.ColumnName] = FactSupport.Expressible,     // [Column]
            [MappingFactCategory.DatabaseType] = FactSupport.Expressible,   // [Column(TypeName = …)]
            [MappingFactCategory.Length] = FactSupport.Expressible,         // [MaxLength]
            [MappingFactCategory.PrecisionAndScale] = FactSupport.Expressible, // [Precision]
            [MappingFactCategory.Nullability] = FactSupport.Expressible,    // [Required]

            // Expressible rather than Required: an entity without a key still produces a
            // usable artifact, as a keyless type. NHibernate has no such fallback, which
            // is where the two frameworks part company.
            [MappingFactCategory.PrimaryKey] = FactSupport.Expressible,

            // [DatabaseGenerated] covers Identity, None and Computed. Sequence, HiLo and
            // the rest are fluent-only, so those individual values fall to diagnostics —
            // the category as a whole is still expressible.
            [MappingFactCategory.PrimaryKeyStrategy] = FactSupport.Expressible,

            [MappingFactCategory.ForeignKeyColumns] = FactSupport.Expressible, // [ForeignKey]
            // [Timestamp] for a version the database produces, [ConcurrencyCheck] for one the
            // framework increments. The annotations have no way to state the increment itself,
            // so that narrowing falls to diagnostics, like the key strategies above.
            [MappingFactCategory.VersionColumn] = FactSupport.Expressible,

            // [Index(nameof(A), IsUnique = true)] is a class-level annotation, which is
            // exactly the surface this builder emits - no fluent configuration is needed
            // for it (decision 055).
            [MappingFactCategory.UniqueConstraint] = FactSupport.Expressible,

            // [NotMapped] takes the property out of the model while it stays on the class
            // (decision 072).
            [MappingFactCategory.TransientProperty] = FactSupport.Expressible,
        },

        // LINQ over DbSet covers every category. Even the full outer join, which EF Core 10
        // has no single operator for, is composed faithfully from LeftJoin, RightJoin and
        // Concat (decision 065), so JoinKind holds no narrowing.
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
            [QueryFeature.SetOperation] = FactSupport.Expressible,
            [QueryFeature.QueryParameter] = FactSupport.Expressible,
            [QueryFeature.Expression] = FactSupport.Expressible,

            // A local variable holding the composed chain, which EF Core 10 turns into a
            // derived table wherever the query refers to it (decision 112, verified).
            [QueryFeature.IntermediateResult] = FactSupport.Expressible,

            // LINQ has no recursion: a variable cannot name itself (decision 113).
            [QueryFeature.Recursion] = FactSupport.NotExpressible,

            // GroupBy over an expression and string.Join over a group, which EF Core 10
            // translates to GROUP BY over a derived table and to STRING_AGG (decision 113,
            // verified against 10.0.10); a key of several parts that no projection names, and
            // a list over a column that may hold NULL or over a subquery, go to native SQL at
            // the point of emission. No ranking function over a window.
            [QueryFeature.ComputedGrouping] = FactSupport.Expressible,
            [QueryFeature.WindowFunction] = FactSupport.NotExpressible,
            [QueryFeature.ListAggregation] = FactSupport.Expressible,
        },

        // EF Core 10 translates every function of the expression vocabulary from the
        // members of System.String and System.DateTime, Math.Abs and ?? (decision 107), and
        // since decision 113 the Add methods of DateTime, EF.Functions.DateDiff…, Math.Round,
        // Math.Sqrt, ToString() and the numeric casts (verified against 10.0.10).
        Functions = QueryFunctionVocabulary.All,

        // What a LINQ chain cannot say - an aggregate over the whole result, a scalar
        // subquery that is not one aggregate - goes out as native SQL (decision 113):
        // SqlQuery into a row class generated beside the method, FromSql for an entity.
        NativeSqlApi = "DatabaseFacade.SqlQuery and DbSet.FromSql",
    };
}