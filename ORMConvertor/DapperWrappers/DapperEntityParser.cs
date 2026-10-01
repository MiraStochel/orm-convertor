using AbstractWrappers;
using CSharpEntityParsing;
using Microsoft.CodeAnalysis;

namespace DapperWrappers;

/// <summary>
/// Parses a Dapper entity class from C# source code. Dapper carries no mapping in the
/// class, so the shared structural reading is the whole parser - but for the one thing it
/// knows about the file around the class (decision 111): a class whose own members call
/// Dapper is the code around queries, found by the same search the query pass reads them by.
/// </summary>
public class DapperEntityParser(AbstractEntityBuilder entityBuilder) : CSharpEntityParser(entityBuilder)
{
    protected override IEnumerable<SyntaxNode> FindHandovers(string source)
        => DapperSqlQueryParser.FindHandovers(source, Limits);
}
