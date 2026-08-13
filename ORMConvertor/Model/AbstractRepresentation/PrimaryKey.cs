namespace Model.AbstractRepresentation;

public sealed class PrimaryKey
{
    private readonly IReadOnlyList<PrimaryKeyPart> parts = [];

    /// <summary>
    /// Key parts, always sorted by <see cref="PrimaryKeyPart.Order"/>, which must be
    /// distinct within one key. The invariants are enforced here rather than at the call
    /// site so that they hold on every construction path, including direct object
    /// initialization. Builders can therefore iterate the list as-is.
    ///
    /// Order need not start at one nor be contiguous: only the relative order carries
    /// meaning and sources number differently (decision 011). Duplicates are rejected,
    /// because with them the resulting order would follow the input rather than the
    /// model - the non-determinism S2 rules out.
    /// </summary>
    public required IReadOnlyList<PrimaryKeyPart> Parts
    {
        get => parts;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == 0)
            {
                throw new ArgumentException("Primary key must have at least one part.", nameof(Parts));
            }

            if (value.Select(p => p.Order).Distinct().Count() != value.Count)
            {
                throw new ArgumentException("Primary key parts must have distinct Order values.", nameof(Parts));
            }

            parts = [.. value.OrderBy(p => p.Order)];
        }
    }

    /// <summary>
    /// Set when the source expressed the key by a dedicated key class. It is a record of
    /// the source, not part of the key definition - the key itself is always the ordered
    /// list of parts above, because every target renders it flat (decision 006).
    /// </summary>
    public SourceKeyClass? SourceKeyClass { get; init; }
}