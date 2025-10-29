using Model.AbstractRepresentation;

namespace AbstractWrappers;
public interface IQueryParser : IParser
{
    void Parse(string source, IReadOnlyList<EntityMap>? entityMaps = null);
}
