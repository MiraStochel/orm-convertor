using AbstractWrappers;
using JakartaPersistence;
using JavaEntityParsing;

namespace EclipseLinkWrappers;

/// <summary>
/// Reads an EclipseLink entity: the shared JPA reading and nothing on top of it, which is
/// what the third hook of decision 076 amounts to here. Not one annotation of
/// org.eclipse.persistence.annotations has a place in the intermediate representation -
/// @AdditionalCriteria and @Multitenant are filters on the model, @CacheIndex and
/// @BatchFetch are about the cache and about loading, @PrivateOwned is cascading - so the
/// reader recognizes none and the shared reading reports each as dropped, exactly as it
/// does for a Hibernate annotation on the way to EclipseLink (decision 080). What it does
/// add is the one warning no artifact can show: a lazy reference under this implementation.
/// </summary>
public sealed class EclipseLinkEntityParser(AbstractEntityBuilder entityBuilder, JpaReadingContext context)
    : JpaEntityParser(entityBuilder, context, new EclipseLinkAnnotationReader());

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
