using AbstractWrappers;
using AbstractWrappers.Descriptors;
using CSharpEntityParsing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DapperWrappers;

/// <summary>
/// Parses a Dapper entity class from C# source code. Dapper carries no mapping in the
/// class, so the shared structural reading is the whole parser - but for what it knows about
/// the file around the class: a class whose own members call Dapper is the code around
/// queries, found by the same search the query pass reads them by (decision 111), and an
/// attribute of Dapper.Contrib is named as the package's, which the tool does not read as
/// Dapper (decision 133).
/// </summary>
public class DapperEntityParser(AbstractEntityBuilder entityBuilder) : CSharpEntityParser(entityBuilder)
{
    protected override IEnumerable<SyntaxNode> FindHandovers(string source)
        => DapperSqlQueryParser.FindHandovers(source, Limits);

    /// <summary>
    /// Dapper's default type map settles a column onto a field of the same name as readily as
    /// onto a property, so a field the reading drops is a mapped member of the class.
    /// </summary>
    protected override string FieldNote
        => "Dapper maps a field by its name the way it maps a property, so a column the field would have "
            + "received is dropped with it.";

    /// <summary>
    /// An attribute of Dapper.Contrib states a mapping fact, so the sentence that Dapper carries
    /// none in the class would mislead; the record says whose the attribute is and what it
    /// stated instead (decision 133). The fact is dropped all the same.
    /// </summary>
    protected override (string Why, MappingFactCategory? Category)? UnreadAttributeNote(AttributeSyntax attribute)
        => DapperContrib.Attribute(attribute);
}
