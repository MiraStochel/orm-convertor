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

        // Pinned by decision 013; the canonical table is in docs/architecture.md. The Java
        // test suite of decision 076 binds this value to the dependency of its pom.xml.
        Version = "7.4.5.Final",

        // Hibernate adds no enforced member of its own to the specification's (decision 077).
        EnforcedMembers = JakartaPersistenceDescriptor.EnforcedMembers,
        Support = JakartaPersistenceDescriptor.Support,
        QuerySupport = JakartaPersistenceDescriptor.QuerySupport,
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
        VendorAnnotationPackage: "org.hibernate.annotations");
}
