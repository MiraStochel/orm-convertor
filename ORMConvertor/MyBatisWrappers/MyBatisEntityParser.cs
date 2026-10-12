using AbstractWrappers;
using AbstractWrappers.Descriptors;
using JavaEntityParsing;

namespace MyBatisWrappers;

/// <summary>
/// Reads a MyBatis domain class, which is the plainest input of all six frameworks: the
/// shared Java reading and nothing on top of it. MyBatis asks a domain class for no
/// annotation, no base class and no interface (decision 084), so there is nothing here to
/// interpret - the mapping of such a class lives in the mapper, which
/// <see cref="MyBatisXmlMappingParser"/> and <see cref="MyBatisMapperInterfaceParser"/>
/// read. The same relation Dapper's entity parser has to the shared C# reading, down to the
/// one thing it adds: an annotation of MyBatis-Plus is named as the package's, which the tool
/// does not read as MyBatis (decision 133).
/// </summary>
public sealed class MyBatisEntityParser(AbstractEntityBuilder entityBuilder) : JavaEntityParser(entityBuilder)
{
    /// <summary>
    /// An annotation of MyBatis-Plus states a mapping fact, so the sentence that MyBatis carries
    /// none in the class would mislead; the record says whose the annotation is and what it
    /// stated instead (decision 133). The fact is dropped all the same.
    /// </summary>
    protected override (string Why, MappingFactCategory? Category)? UnreadAnnotationNote(JavaAnnotation annotation)
        => MyBatisPlus.Annotation(annotation, Imports);
}
