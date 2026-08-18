namespace DatabaseCatalog;

/// <summary>
/// The one place the junction criterion of decision 005 is written down for the catalog:
/// a table is junction-shaped when its whole primary key consists of the columns of
/// exactly two foreign keys. Columns beyond the key do not disqualify it - a "rich"
/// junction table carries payload next to the association.
/// </summary>
public static class JunctionShape
{
    /// <summary>
    /// The two foreign keys forming the primary key, or null when the table is not
    /// junction-shaped.
    /// </summary>
    public static (ForeignKeyImage First, ForeignKeyImage Second)? TryGet(TableImage image)
    {
        if (image.PrimaryKeyColumns.Count == 0)
        {
            return null;
        }

        var key = new HashSet<string>(image.PrimaryKeyColumns, StringComparer.OrdinalIgnoreCase);

        // Only foreign keys living entirely inside the key can form it; a foreign key
        // reaching outside (OrderLines → Products) marks an ordinary entity.
        var insideKey = image.ForeignKeys
            .Where(fk => fk.Columns.All(c => key.Contains(c.Column)))
            .ToList();

        if (insideKey.Count != 2)
        {
            return null;
        }

        var covered = insideKey
            .SelectMany(fk => fk.Columns.Select(c => c.Column))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return covered.SetEquals(key) ? (insideKey[0], insideKey[1]) : null;
    }
}
