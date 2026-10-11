using AbstractWrappers.Descriptors;
using Model;

namespace AbstractWrappers.Diagnostics;

/// <summary>
/// Where a mapping fact stands in the input unit that stated it (decision 125): the unit as
/// records reference units (decision 066) and the range of characters in its content. One
/// per fact, because a fact is written once under source precedence (decision 017).
/// </summary>
public sealed record SourcePosition(string Unit, int Start, int Length);

/// <summary>
/// Where a mapping fact stands in a generated artifact (decision 125): the artifact by its
/// content type - unique among the artifacts of one entity - and the range of characters in
/// its content. A fact may show itself in several places of one artifact, or in both artifacts
/// of a framework that writes a class and a mapping document, so there are as many as the
/// builder stated.
/// </summary>
public sealed record TargetPosition(ConversionContentType Artifact, int Start, int Length);

/// <summary>
/// A finer position inside a reading scope (<see cref="AbstractEntityBuilder.At"/>): the facts
/// of this category - and of this property, or of any property where <paramref name="Property"/>
/// is null - that the scope creates stand at this range rather than at the scope's own. A
/// parser reading an element with several attributes names each attribute's range this way.
/// </summary>
public sealed record FactRefinement(MappingFactCategory Category, string? Property, TextRange Range)
{
    /// <summary>A refinement for the category whatever the property.</summary>
    public FactRefinement(MappingFactCategory category, TextRange range)
        : this(category, null, range)
    {
    }
}
