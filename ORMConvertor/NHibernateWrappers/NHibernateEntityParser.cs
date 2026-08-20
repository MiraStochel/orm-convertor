using AbstractWrappers;
using CSharpEntityParsing;

namespace NHibernateWrappers;

/// <summary>
/// Parses an NHibernate entity class from C# source code. The class carries no mapping —
/// that lives in the XML artifact read by <see cref="NHibernateXMLMappingParser"/> — so
/// the shared structural reading is the whole parser.
/// </summary>
public class NHibernateEntityParser(AbstractEntityBuilder entityBuilder) : CSharpEntityParser(entityBuilder);
