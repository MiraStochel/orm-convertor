using AbstractWrappers.Descriptors;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;

namespace HibernateWrappers;

/// <summary>
/// Hibernate ORM as a target (decision 077): the enforced members and the support of the
/// shared Jakarta Persistence layer, the pinned release, and beside the descriptor the
/// profile of the implementation - what the release number cannot say (decision 076).
/// </summary>
public static class HibernateDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.Hibernate,
        Ecosystem = Ecosystem.Java,

        // Pinned by decision 013; the canonical table is in docs/architecture.md. The Java
        // test suite of decision 076 binds this value to the dependency of its pom.xml.
        Version = "7.4.5.Final",

        // The database system these artifacts are written for (decision 086); the
        // only dialect this version targets, declared rather than assumed.
        Dialect = DatabaseDialect.SqlServer2022,

        // Hibernate adds no enforced member of its own to the specification's (decision 077).
        EnforcedMembers = JakartaPersistenceDescriptor.EnforcedMembers,
        Support = JakartaPersistenceDescriptor.Support,

        // HQL 7.4 adds WITH and a subquery in from and after join to JPQL (decision 112,
        // verified against this release), and a WITH that names itself, which it writes as
        // a recursive common table expression of SQL Server (decision 113, verified against
        // 7.4.5 over SQL Server: the definition read from from and from join, a counter,
        // a read from a subquery, a recursive definition over an earlier one). It groups by
        // an expression, ranks over a window and joins a list with listagg, which it writes
        // as STRING_AGG (decision 113, verified against 7.4.5 over SQL Server, a window
        // inside a WITH filtered outside it and listagg in a correlated subquery included).
        QuerySupport = JakartaPersistenceDescriptor.QuerySupportWith(
            QueryFeature.IntermediateResult,
            QueryFeature.Recursion,
            QueryFeature.ComputedGrouping,
            QueryFeature.WindowFunction,
            QueryFeature.ListAggregation),

        // The whole vocabulary: the specification's functions, and timestampadd and
        // timestampdiff, which HQL 7.4 writes as DATEADD and as DATEDIFF_BIG over SQL Server -
        // the number of boundaries, as T-SQL counts them (decision 113, verified against
        // 7.4.5 with moments whose count of boundaries and length differ).
        Functions = QueryFunctionVocabulary.All,
        NativeSqlApi = JakartaPersistenceDescriptor.NativeSqlApi,
    };

    /// <summary>
    /// Verified in the Hibernate tutorial and the comparison of the Java frameworks: AUTO
    /// is a sequence named after the entity with a step of 50, implicit names follow the
    /// JPA-compliant strategy with no physical transformation (Spring Boot's snake_case is
    /// its own doing), and a String is varchar unless @Nationalized says otherwise.
    /// </summary>
    public static JpaImplementationProfile Profile { get; } = new(
        Implementation: ORMEnum.Hibernate,
        SpecificationLevel: JakartaPersistenceDescriptor.SpecificationLevel,
        AutoStrategy: PrimaryKeyStrategy.Sequence,
        DefaultAllocationSize: 50,
        NamingStrategy: "ImplicitNamingStrategyJpaCompliantImpl, PhysicalNamingStrategyStandardImpl",
        NationalizedByDefault: false,
        UppercaseImplicitNames: false,
        VendorAnnotationPackage: "org.hibernate.annotations",

        // fetch = LAZY on a reference does what it says here: the proxy is a subclass and
        // needs nothing of the consumer project (decision 080).
        LazyReferenceNeedsWeaving: false,

        // The counter table of a TABLE generator stays unstated because no run of ours has
        // measured what Hibernate 7.4.5 creates for it; what the tutorial measured was AUTO,
        // and AUTO is a sequence here. An unmeasured default is not ours to write down
        // (decision 080), so a TABLE generator without parameters keeps the target's own.
        DefaultCounterTable: null,

        // Measured against 7.4.5.Final when the escape path was written (decision 113): a
        // List bound to ?1 of a native query is expanded into the list IN (?1) ranges over,
        // and a position named twice binds one value twice.
        NativeQueryExpandsCollection: true,

        // Measured against 7.4.5.Final with every identifier of the JPQL grammar and the
        // keywords of HQL as an entity name - at the head of a from clause, as the target of
        // an inner and a left entity join and in a subquery: the parser takes every one of
        // them for a name but the three literals, which it refuses wherever they stand.
        EntityNamesRefused: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false", "null" });
}
