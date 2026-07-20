using Model;

namespace AbstractWrappers;

public interface IParser
{
    void Parse(string source);

    bool CanParse(ConversionContentType contentType);
}
