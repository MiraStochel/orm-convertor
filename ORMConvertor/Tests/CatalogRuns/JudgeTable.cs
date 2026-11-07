using Microsoft.Data.SqlClient;

namespace Tests.CatalogRuns;

/// <summary>
/// The judge of a catalog as its database holds it (decisions 149 and 208): per query the
/// digest of its result at position 0 and, where the result is small enough, the rows
/// themselves. Read by the on-demand run and by the path of a foreign artifact that answers
/// a query of the catalog (decision 172), which is measured against the same judge the tool's
/// artifact is.
/// </summary>
internal static class JudgeTable
{
    /// <summary>The judge by query key, or null where the database holds no judge yet.</summary>
    public static Dictionary<string, (string Digest, List<string> Rows)>? Read(SqlConnection control)
    {
        using (var exists = control.CreateCommand())
        {
            exists.CommandText = $"SELECT OBJECT_ID(N'[{CatalogDatabase.Schema}].[Judge]', N'U')";
            if (exists.ExecuteScalar() is null or DBNull)
            {
                return null;
            }
        }

        var judge = new Dictionary<string, (string Digest, List<string> Rows)>(StringComparer.OrdinalIgnoreCase);

        using var command = control.CreateCommand();
        command.CommandText = $"SELECT [QueryKey], [Position], [Rendered] FROM [{CatalogDatabase.Schema}].[Judge] ORDER BY [QueryKey], [Position];";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!judge.TryGetValue(key, out var entry))
            {
                judge[key] = entry = (string.Empty, []);
            }

            if (reader.GetInt32(1) == 0)
            {
                judge[key] = (reader.GetString(2), entry.Rows);
            }
            else
            {
                entry.Rows.Add(reader.GetString(2));
            }
        }

        return judge;
    }

    /// <summary>
    /// Whether rows in the canonical form agree with the judge's entry: by the digest, which
    /// every entry has, and by the rows where the judge keeps them. Null where the judge has
    /// no entry for the key - the run it was made from did not run the query.
    /// </summary>
    public static bool? Agrees(IReadOnlyDictionary<string, (string Digest, List<string> Rows)> judge, string key, IReadOnlyList<string> rows)
    {
        if (!judge.TryGetValue(key, out var entry))
        {
            return null;
        }

        return CatalogRun.Digest(rows) == entry.Digest && (entry.Rows.Count == 0 || entry.Rows.SequenceEqual(rows));
    }
}
