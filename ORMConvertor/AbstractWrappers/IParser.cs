using Model;

namespace AbstractWrappers;

/// <summary>
/// What every parser answers regardless of what it reads: whether it accepts a unit of the
/// given content type. The orchestration asks this before handing a unit over, so no parser
/// ever has to recognize the language of its input from the text (decision 025).
/// </summary>
public interface IParser
{
    bool CanParse(ConversionContentType contentType);
}

/// <summary>
/// Reads an entity or a mapping unit into the entity builder. Kept apart from
/// <see cref="IQueryParser"/> so that a query parser does not carry a Parse overload it must
/// never answer, and so that the orchestration can select entity parsers by name rather than
/// by excluding query ones (decision 047).
/// </summary>
public interface IEntityParser : IParser
{
    void Parse(string source);
}
