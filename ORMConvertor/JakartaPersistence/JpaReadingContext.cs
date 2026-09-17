namespace JakartaPersistence;

/// <summary>
/// What the two parsers of one JPA source framework share within one conversion
/// (decision 077): which classes orm.xml declared metadata-complete, so that the entity
/// parser reading after it - orm.xml outranks the annotations (decision 068) - takes only
/// the language facts from their annotations; and which attribute orm.xml declared the
/// embedded id, whose key class only the Java declaration can name. Created by the
/// wrapper's factory row and handed to both parsers; the orchestration and the abstract
/// wrappers never see it.
/// </summary>
public sealed class JpaReadingContext
{
    private readonly HashSet<string> metadataCompleteClasses = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> embeddedIds = new(StringComparer.Ordinal);

    /// <summary>xml-mapping-metadata-complete: every class of the conversion.</summary>
    public bool AllMetadataComplete { get; set; }

    public void MarkMetadataComplete(string className) => metadataCompleteClasses.Add(className);

    public bool IsMetadataComplete(string className)
        => AllMetadataComplete || metadataCompleteClasses.Contains(className);

    /// <summary>orm.xml declared the attribute the embedded id of the class; the class declaration supplies the key class.</summary>
    public void MarkEmbeddedId(string className, string attributeName) => embeddedIds[className] = attributeName;

    public string? EmbeddedIdOf(string className)
        => embeddedIds.TryGetValue(className, out var attribute) ? attribute : null;
}
