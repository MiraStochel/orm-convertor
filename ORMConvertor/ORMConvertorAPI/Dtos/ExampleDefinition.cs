using Model;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// One example as a whole conversion input (decision 099): the direction, and the units the
/// explanatory page sends to <c>/convert</c> unchanged and the translation screen loads into
/// its editor. Each unit carries the name of the file it stands for, so the records of the
/// run point back at that file (decision 066). The key is the anchor of the explanatory
/// page's section and what the page finds the example by; the title is the example's name
/// where a page lists the examples, without the direction, which the list states from the
/// two frameworks. The prose around an example is the explanatory page's own.
/// </summary>
public record ExampleDefinition(
    string Key,
    string Title,
    ORMEnum SourceOrm,
    ORMEnum TargetOrm,
    List<ConversionSource> Units
);
