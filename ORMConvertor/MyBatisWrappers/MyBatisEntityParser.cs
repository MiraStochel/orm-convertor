using AbstractWrappers;
using JavaEntityParsing;

namespace MyBatisWrappers;

/// <summary>
/// Reads a MyBatis domain class, which is the plainest input of all six frameworks: the
/// shared Java reading and nothing on top of it. MyBatis asks a domain class for no
/// annotation, no base class and no interface (decision 084), so there is nothing here to
/// interpret - the mapping of such a class lives in the mapper, which
/// <see cref="MyBatisXmlMappingParser"/> and <see cref="MyBatisMapperInterfaceParser"/>
/// read. The same relation Dapper's entity parser has to the shared C# reading.
/// </summary>
public sealed class MyBatisEntityParser(AbstractEntityBuilder entityBuilder) : JavaEntityParser(entityBuilder);
