using AbstractWrappers.Descriptors;
using JakartaPersistence;

namespace HibernateWrappers;

/// <summary>
/// Emits JPQL for Hibernate (decision 077). The shared JPA query builder writes standard
/// JPQL 3.2 with the entity join both implementations add; Hibernate needs nothing on top
/// of it, so the class states only whose descriptor the records carry and whose profile its
/// native SQL is written for (decision 113).
/// </summary>
public sealed class HibernateJpqlQueryBuilder : AbstractJpaQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => HibernateDescriptor.Instance;

    protected override JpaImplementationProfile Profile => HibernateDescriptor.Profile;
}
