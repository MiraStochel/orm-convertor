using Model;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// One example of the explanatory page as a whole conversion input (decision 099): the
/// direction, and the units the page sends to <c>/convert</c> unchanged. Each unit carries
/// the name of the file it stands for, so the records of the run point back at that file
/// (decision 066). The key is the anchor of the page's section and what the page finds the
/// example by; the prose around it is the page's own.
/// </summary>
public record ExampleDefinition(
    string Key,
    ORMEnum SourceOrm,
    ORMEnum TargetOrm,
    List<ConversionSource> Units
);
