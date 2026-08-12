namespace Model.AbstractRepresentation;

public sealed class PrimaryKey
{
    private readonly IReadOnlyList<PrimaryKeyPart> parts = [];

    /// <summary>
    /// Key parts, always sorted by <see cref="PrimaryKeyPart.Order"/>.
    /// The invariant is enforced here rather than at the call site so that it holds
    /// on every construction path, including direct object initialization. Builders
    /// can therefore iterate the list as-is.
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