namespace Model.AbstractRepresentation;

/// <summary>
/// A unique constraint of one entity (decision 055). It lives on <see cref="EntityMap"/>
/// rather than on a property map because it may cover several columns - the same reason
/// <see cref="Relation"/> sits there (decision 001).
///
/// The parts are named by <b>property</b>, not by column and not by object reference.
/// By property because both targets ask for exactly that - <c>nameof(Sku)</c> in EF Core
/// and the name attribute of a &lt;property&gt; element in NHibernate - while the column is
/// what the catalog knows and what the completion phase translates. By name because key
/// classes dissolve, junction entities appear and names resolve between reading and
/// emission, and a held reference would drift out of step with the model.
/// </summary>
public sealed class UniqueConstraint
{
    private readonly IReadOnlyList<string> propertyNames = [];

    /// <summary>
    /// Name of the constraint as the source or the catalog states it; null when it has
    /// none. NHibernate's unique="true" and an unnamed [Index(…, IsUnique = true)] state
    /// the constraint without naming it, and inventing a name would claim what neither
    /// said (decision 055).
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Property names the constraint covers, in the order the source states them. The
    /// invariants are enforced here so that they hold on every construction path: an
    /// empty constraint constrains nothing and a repeated property would make the same
    /// constraint compare unequal to itself.
    /// </summary>
    public required IReadOnlyList<string> PropertyNames
    {
        get => propertyNames;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count == 0)
            {
                throw new ArgumentException("A unique constraint must cover at least one property.", nameof(PropertyNames));
            }

            if (value.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException("A unique constraint cannot cover an unnamed property.", nameof(PropertyNames));
            }

            if (value.Distinct(StringComparer.Ordinal).Count() != value.Count)
            {
                throw new ArgumentException("A unique constraint cannot cover the same property twice.", nameof(PropertyNames));
            }

            propertyNames = [.. value];
        }
    }

    /// <summary>
    /// Whether two constraints cover the same properties. Identity is the set, not the
    /// name: the source and the catalog may spell the name differently while stating one
    /// and the same constraint, and it is the set that decides (decision 055).
    /// </summary>
    public bool CoversSameAs(UniqueConstraint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return PropertyNames.Count == other.PropertyNames.Count
            && PropertyNames.ToHashSet(StringComparer.Ordinal).SetEquals(other.PropertyNames);
    }
}
