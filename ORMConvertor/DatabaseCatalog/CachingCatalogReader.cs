namespace DatabaseCatalog;

/// <summary>
/// Remembers what an inner reader answered for as long as the decorator lives; the owner
/// chooses the lifetime - one Advisor run, one test collection - and with it the claim
/// that the catalog does not change underneath it. Table resolution is cached per request
/// (schema and candidate list, case-insensitively), so an overlapping batch sends the
/// inner reader only the requests it has not answered yet; the junction probe is cached
/// per set of referenced tables. Negative answers are cached too - an absent table stays
/// absent for the lifetime. One lock serializes the calls, because the inner reader holds
/// a single connection.
/// </summary>
public sealed class CachingCatalogReader(ICatalogReader inner) : ICatalogReader, IDisposable
{
    private readonly Dictionary<string, TableLookup> lookups = [];
    private readonly Dictionary<string, IReadOnlyList<TableImage>> junctions = [];
    private readonly object gate = new();

    public IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests)
    {
        lock (gate)
        {
            var results = new Dictionary<string, TableLookup>();
            var misses = new List<TableRequest>();

            foreach (var request in requests)
            {
                if (lookups.TryGetValue(ResolutionKey(request), out var known))
                {
                    results[request.Key] = known;
                }
                else
                {
                    misses.Add(request);
                }
            }

            if (misses.Count > 0)
            {
                var loaded = inner.ReadTables(misses);

                foreach (var request in misses)
                {
                    // A request without candidates gets no entry from the reader, and so
                    // none from the cache either.
                    if (loaded.TryGetValue(request.Key, out var lookup))
                    {
                        lookups[ResolutionKey(request)] = lookup;
                        results[request.Key] = lookup;
                    }
                }
            }

            return results;
        }
    }

    public IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables)
    {
        lock (gate)
        {
            var key = string.Join("|", referencedTables
                .Select(t => t.QualifiedName.ToLowerInvariant())
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal));

            if (!junctions.TryGetValue(key, out var found))
            {
                junctions[key] = found = inner.FindJunctionTables(referencedTables);
            }

            return found;
        }
    }

    public void Dispose() => (inner as IDisposable)?.Dispose();

    /// <summary>
    /// What a lookup's answer depends on: the stated schema and the candidate list in
    /// order. The request's own key is the caller's name for the result, not part of it.
    /// </summary>
    private static string ResolutionKey(TableRequest request)
        => $"{request.Schema?.ToLowerInvariant() ?? "*"}|{string.Join("|", request.NameCandidates.Select(n => n.ToLowerInvariant()))}";
}
