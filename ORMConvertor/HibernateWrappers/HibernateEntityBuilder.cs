using AbstractWrappers.Descriptors;
using JakartaPersistence;
using Model.AbstractRepresentation;

namespace HibernateWrappers;

/// <summary>
/// Emits a Hibernate entity: the shared JPA output with Hibernate's profile (decision 077).
/// The one thing Hibernate spells differently from the specification is national
/// character data - @Nationalized from org.hibernate.annotations - which is the second
/// hook of decision 076.
/// </summary>
public sealed class HibernateEntityBuilder : AbstractJpaEntityBuilder
{
    public override TargetFrameworkDescriptor Descriptor => HibernateDescriptor.Instance;

    protected override JpaImplementationProfile Profile => HibernateDescriptor.Profile;

    protected override void AppendNationalization(
        EntityMap entityMap, PropertyMap propertyMap, List<string> arguments, List<string> annotations)
    {
        Import($"{Profile.VendorAnnotationPackage}.Nationalized");
        annotations.Add("    @Nationalized");
    }
}
