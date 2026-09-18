using AbstractWrappers.Descriptors;
using JakartaPersistence;

namespace EclipseLinkWrappers;

/// <summary>
/// Emits JPQL for EclipseLink (decision 080). The shared JPA query builder writes standard
/// JPQL 3.2 with the entity join both implementations add; EclipseLink needs nothing on
/// top of it, so the class states only whose descriptor the records carry - the same shape
/// the Hibernate query builder has, and for the same reason.
/// </summary>
public sealed class EclipseLinkJpqlQueryBuilder : AbstractJpaQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EclipseLinkDescriptor.Instance;
}
