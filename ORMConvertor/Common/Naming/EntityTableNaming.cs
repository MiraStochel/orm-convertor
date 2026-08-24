namespace Common.Naming;

/// <summary>
/// The convention between an entity name and a table name, in both directions and in one
/// place (decision 050). Deliberately the crude trailing-"s" rule rather than a pluralization
/// library: the derivation is always reported as a convention of the tool, and a rule the
/// user can predict is worth more here than a rule that is right more often.
///
/// Lives in Common because both directions are needed on the far side of S1 - the catalog
/// asks entity to table, the builders ask table to entity - and neither may depend on the
/// other's project.
/// </summary>
public static class EntityTableNaming
{
    /// <summary>
    /// The entity name a table name suggests: the singular of a plural, the name itself
    /// otherwise. A one-character name keeps its character - an empty name is not an answer.
    /// </summary>
    public static string EntityNameFor(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);

        var bare = BareName(tableName);

        return HasPluralSuffix(bare) ? bare[..^1] : bare;
    }

    /// <summary>
    /// The table names an entity name suggests, in the order they are to be tried: the name
    /// as written first, because a source that states the table states it exactly, and the
    /// other number after it.
    /// </summary>
    public static IReadOnlyList<string> TableCandidatesFor(string entityName)
    {
        ArgumentNullException.ThrowIfNull(entityName);

        var candidates = new List<string> { entityName };

        if (HasPluralSuffix(entityName))
        {
            candidates.Add(entityName[..^1]);
        }
        else if (!EndsInS(entityName))
        {
            // A single "s" is the only name that is neither plural enough to shorten nor
            // singular enough to lengthen; it gets no second candidate.
            candidates.Add(entityName + "s");
        }

        return candidates;
    }

    /// <summary>
    /// The bare name of a qualified one: the schema belongs to the table, never to the
    /// entity, so it is dropped before the convention is applied.
    /// </summary>
    public static string BareName(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);

        return tableName.Split('.').LastOrDefault() ?? tableName;
    }

    private static bool HasPluralSuffix(string name) => name.Length > 1 && EndsInS(name);

    private static bool EndsInS(string name) => name.EndsWith('s') || name.EndsWith('S');
}
