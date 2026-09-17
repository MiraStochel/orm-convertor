using AbstractWrappers;
using JakartaPersistence;
using JavaEntityParsing;

namespace HibernateWrappers;

/// <summary>
/// Reads a Hibernate entity: the shared JPA reading plus Hibernate's own annotations - the
/// third hook of decision 076. @Nationalized has a place in the model (the unicode facet
/// of decision 019); every other org.hibernate.annotations annotation - @Formula,
/// @NaturalId, @JdbcTypeCode, @ColumnDefault, @Type - has none and is reported as
/// dropped, as it would be between NHibernate and EF Core.
/// </summary>
public sealed class HibernateEntityParser(AbstractEntityBuilder entityBuilder, JpaReadingContext context)
    : JpaEntityParser(entityBuilder, context, new HibernateAnnotationReader());

public sealed class HibernateAnnotationReader : JpaAnnotationReader
{
    protected override bool ReadVendorAnnotation(JavaAnnotation annotation, JpaEntityFacts? entity, JpaAttributeFacts? attribute)
    {
        if (attribute is not null && annotation.SimpleName == "Nationalized"
            && (annotation.Qualifier is null || annotation.Qualifier == HibernateDescriptor.Profile.VendorAnnotationPackage))
        {
            attribute.Nationalized = true;
            return true;
        }

        return false;
    }
}
