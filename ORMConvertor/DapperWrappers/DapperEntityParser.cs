using AbstractWrappers;
using CSharpEntityParsing;

namespace DapperWrappers;

/// <summary>
/// Parses a Dapper entity class from C# source code. Dapper carries no mapping in the
/// class, so the shared structural reading is the whole parser.
/// </summary>
public class DapperEntityParser(AbstractEntityBuilder entityBuilder) : CSharpEntityParser(entityBuilder);
