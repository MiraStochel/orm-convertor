using AbstractWrappers.Descriptors;
using JakartaPersistence;
using Model;
using Model.AbstractRepresentation.Enums;

namespace EclipseLinkWrappers;

/// <summary>
/// EclipseLink as a target (decision 080): the enforced members and the support of the
/// shared Jakarta Persistence layer, the pinned release, and beside the descriptor the
/// profile of the implementation - what the release number cannot say (decision 076),
/// and what the second implementation makes visible, because every value below differs
/// from Hibernate's behind the very same annotations.
/// </summary>
public static class EclipseLinkDescriptor
{
    public static TargetFrameworkDescriptor Instance { get; } = new()
    {
        Framework = ORMEnum.EclipseLink,

        // Pinned by decision 013; the canonical table is in docs/architecture.md. The Java
        // test suite of decision 076 binds this value to the dependency of its pom.xml.
        Version = "5.0.0",

        // EclipseLink adds no enforced member of its own to the specification's: its
        // indirection rewrites the entity rather than subclassing it, so it does not even
        // need the non-final class §2.1 demands anyway (decision 080).
        EnforcedMembers = JakartaPersistenceDescriptor.EnforcedMembers,
        Support = JakartaPersistenceDescriptor.Support,
        QuerySupport = JakartaPersistenceDescriptor.QuerySupport,
    };

    /// <summary>
    /// Measured in the EclipseLink tutorial, in the DDL its 5.0.0 release generated from
    /// annotations that state nothing: AUTO is a counter table named SEQUENCE with the row
    /// SEQ_GEN, an implicit name is written in upper case, and a String is VARCHAR with no
    /// switch anywhere to make it national - only a literal columnDefinition will. To that
    /// the ninth step of the same tutorial added the fact no artifact shows: fetch = LAZY on
    /// a reference is honoured only where the consumer project weaves its bytecode.
    /// </summary>
    public static JpaImplementationProfile Profile { get; } = new(
        Implementation: ORMEnum.EclipseLink,
        SpecificationLevel: JakartaPersistenceDescriptor.SpecificationLevel,
        AutoStrategy: PrimaryKeyStrategy.HiLo,
        DefaultAllocationSize: 50,
        NamingStrategy: "JPA-compliant implicit names, written upper case (eclipselink.jpa.uppercase-column-names)",
        NationalizedByDefault: false,
        UppercaseImplicitNames: true,
        VendorAnnotationPackage: "org.eclipse.persistence.annotations",
        LazyReferenceNeedsWeaving: true,
        DefaultCounterTable: new JpaCounterTable("SEQUENCE", "SEQ_NAME", "SEQ_COUNT", "SEQ_GEN"));
}
