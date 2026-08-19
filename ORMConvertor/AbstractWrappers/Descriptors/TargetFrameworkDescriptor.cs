using Model;
using Model.AbstractRepresentation;

namespace AbstractWrappers.Descriptors;

/// <summary>
/// Single place stating what a target framework requires, can express, and adds to the
/// generated artifact. Consumed by builders during generation, by orchestration before
/// generation to assemble the catalog demand, and by diagnostics to report facts that
/// did not survive the translation.
/// </summary>
public sealed class TargetFrameworkDescriptor
{
    private readonly IReadOnlyDictionary<MappingFactCategory, FactSupport> support =
        new Dictionary<MappingFactCategory, FactSupport>();

    public required ORMEnum Framework { get; init; }

    private readonly IReadOnlyList<EnforcedMember> enforcedMembers = [];

    /// <summary>
    /// Members the builder adds on top of the domain. Empty for frameworks that impose
    /// nothing, which is a statement in itself rather than an omission.
    /// </summary>
    public IReadOnlyList<EnforcedMember> EnforcedMembers
    {
        get => enforcedMembers;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            foreach (var member in value)
            {
                member.Validate();
            }

            enforcedMembers = value;
        }
    }

    /// <summary>
    /// Support level for every category. Completeness is enforced here so that adding a
    /// category fails loudly on every descriptor instead of defaulting silently.
    /// </summary>
    public required IReadOnlyDictionary<MappingFactCategory, FactSupport> Support
    {
        get => support;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            var missing = Enum.GetValues<MappingFactCategory>()
                .Where(category => !value.ContainsKey(category))
                .ToList();

            if (missing.Count > 0)
            {
                throw new ArgumentException(
                    $"Descriptor is missing a support level for: {string.Join(", ", missing)}.",
                    nameof(Support));
            }

            support = value;
        }
    }

    public FactSupport SupportOf(MappingFactCategory category) => support[category];

    private readonly IReadOnlyDictionary<QueryFeature, FactSupport> querySupport =
        new Dictionary<QueryFeature, FactSupport>();

    /// <summary>
    /// Support level for every query feature (decision 022). Completeness is enforced the
    /// same way as for mapping facts, so adding a feature fails loudly on every descriptor
    /// instead of defaulting to silence — which is exactly what a capability report must
    /// never do.
    /// </summary>
    public required IReadOnlyDictionary<QueryFeature, FactSupport> QuerySupport
    {
        get => querySupport;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            var missing = Enum.GetValues<QueryFeature>()
                .Where(feature => !value.ContainsKey(feature))
                .ToList();

            if (missing.Count > 0)
            {
                throw new ArgumentException(
                    $"Descriptor is missing a support level for: {string.Join(", ", missing)}.",
                    nameof(QuerySupport));
            }

            querySupport = value;
        }
    }

    public FactSupport SupportOf(QueryFeature feature) => querySupport[feature];

    /// <summary>
    /// Enforced members applying to a given entity map.
    /// </summary>
    public IEnumerable<EnforcedMember> EnforcedMembersFor(EntityMap entityMap)
    {
        ArgumentNullException.ThrowIfNull(entityMap);

        return EnforcedMembers.Where(member => Applies(member.Condition, entityMap));
    }

    private static bool Applies(EnforcedMemberCondition condition, EntityMap entityMap) => condition switch
    {
        EnforcedMemberCondition.Always => true,
        EnforcedMemberCondition.CompositePrimaryKey => entityMap.PrimaryKey is { Parts.Count: > 1 },
        EnforcedMemberCondition.NoPrimaryKey => entityMap.PrimaryKey is null,
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null),
    };
}