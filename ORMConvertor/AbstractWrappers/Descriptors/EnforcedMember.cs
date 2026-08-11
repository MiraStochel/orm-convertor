namespace AbstractWrappers.Descriptors;

/// <summary>
/// Something the target framework forces onto a generated artifact even though it states
/// nothing about the translated domain — NHibernate's virtual members, its composite-key
/// identity members and non-sealed class, EF Core's keyless marker, JPA's ID class.
///
/// Imports are deliberately not modelled here: a using directive follows from an element
/// that is emitted, so it belongs to the builder that emits it, not to the declaration of
/// what the framework demands.
/// </summary>
public sealed class EnforcedMember
{
    /// <summary>
    /// Human-readable name used in diagnostics, e.g. "virtual on mapped members".
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// When the member applies.
    /// </summary>
    public required EnforcedMemberCondition Condition { get; init; }

    /// <summary>
    /// Text that must appear in the artifact when the condition holds. May contain the
    /// placeholder <c>{ClassName}</c>. Null when the requirement is expressed only by
    /// <see cref="ForbiddenMarker"/>. A substring check is a deliberate ceiling: it catches
    /// a member that is missing altogether, not one that is missing from a single property.
    /// </summary>
    public string? Marker { get; init; }

    /// <summary>
    /// Text that must not appear in the artifact when the condition holds. This is how
    /// negative requirements are stated — a class that must stay non-sealed, or a type
    /// that must keep its implicit parameterless constructor. Without it those two would
    /// remain satisfied by accident, which is exactly what decision 009 set out to end.
    /// </summary>
    public string? ForbiddenMarker { get; init; }

    /// <summary>
    /// Why the framework needs it. Kept next to the member so the reason travels with the
    /// declaration instead of living in a comment at the emission site.
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Resolves the placeholders in a marker against a concrete entity class name.
    /// </summary>
    public static string? Resolve(string? marker, string className)
        => marker?.Replace("{ClassName}", className, StringComparison.Ordinal);

    /// <summary>
    /// Guards against a member that asserts nothing.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Marker) && string.IsNullOrWhiteSpace(ForbiddenMarker))
        {
            throw new InvalidOperationException(
                $"Enforced member '{Name}' declares neither a marker nor a forbidden marker.");
        }
    }
}