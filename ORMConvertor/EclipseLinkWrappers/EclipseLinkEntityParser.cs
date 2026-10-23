using AbstractWrappers;
using JakartaPersistence;
using JavaEntityParsing;
using Model.AbstractRepresentation.Enums;

namespace EclipseLinkWrappers;

/// <summary>
/// Reads an EclipseLink entity: the shared JPA reading and nothing on top of it, which is
/// what the third hook of decision 076 amounts to here. Not one annotation of
/// org.eclipse.persistence.annotations has a place in the intermediate representation -
/// @AdditionalCriteria and @Multitenant are filters on the model, @CacheIndex and
/// @BatchFetch are about the cache and about loading, @PrivateOwned is cascading - so the
/// reader recognizes none and the shared reading reports each as dropped, exactly as it
/// does for a Hibernate annotation on the way to EclipseLink (decision 080). What it does
/// add is the one warning no artifact can show: a lazy reference under this implementation,
/// and the one fact of reading no annotation states: the value of a fixed-width column comes
/// to the program without its trailing spaces (decision 169).
/// </summary>
public sealed class EclipseLinkEntityParser(AbstractEntityBuilder entityBuilder, JpaReadingContext context)
    : JpaEntityParser(entityBuilder, context, new EclipseLinkAnnotationReader())
{
    /// <summary>
    /// Under its default session EclipseLink 5.0.0 hands the value of every CHAR and NCHAR
    /// column over without its trailing characters up to U+0020 (decision 169, measured by the
    /// Java suite's CharTrimmingClaimsTest). The session is the consumer's (decision 040), so
    /// the default is what the program was written against; whether a column is fixed-width
    /// is often known only after the catalog phase, so every text property carries the fact
    /// and the builders ask for it together with the type.
    /// </summary>
    protected override void ParseClassBody(JavaClass cls)
    {
        base.ParseClassBody(cls);

        foreach (var propertyMap in entityBuilder.EntityMap.PropertyMaps.Where(p => p.Property.Type?.ScalarType == ScalarType.String))
        {
            propertyMap.TrimmedOnRead = true;
        }
    }
}

public sealed class EclipseLinkAnnotationReader : JpaAnnotationReader
{
    /// <summary>
    /// The loading strategy stays out of the model (the paper, §5.4) and Hibernate says
    /// nothing about it (decision 077). Here the same line is not merely dropped from the
    /// translation: under EclipseLink it was inert in the source as well unless that
    /// project ran the weaving agent, so the record says both halves at once.
    /// </summary>
    protected override void ReadLazyReference(JavaAnnotation annotation, JpaAttributeFacts facts)
        => facts.Notes.Add(
            $"The reference states {annotation.SimpleName}(fetch = LAZY). The loading strategy has no place in the "
            + "intermediate representation and is dropped; under EclipseLink it takes effect only where the consumer "
            + "project weaves its bytecode - without the agent the reference is loaded eagerly and nothing says so "
            + "(decision 080).");
}
