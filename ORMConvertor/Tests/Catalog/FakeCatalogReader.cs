using DatabaseCatalog;

namespace Tests.Catalog;

/// <summary>
/// A catalog made of prepared table images, so the completion phase - the control side of
/// decision 015 - can be tested without a database. The mechanism side, the SQL Server
/// reader, has its own database-dependent test.
/// </summary>
internal sealed class FakeCatalogReader(params TableImage[] images) : ICatalogReader
{
    public int Reads { get; private set; }

    public IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests)
    {
        Reads++;

        var results = new Dictionary<string, TableLookup>();

        foreach (var request in requests)
        {
            results[request.Key] = Resolve(request);
        }

        return results;
    }

    /// <summary>
    /// The first candidate with a match wins. A name found in more than one schema when the
    /// request states none answers the way the SQL Server reader answers: dbo if it is one
    /// of them, otherwise the ambiguity is returned rather than guessed at.
    /// </summary>
    private TableLookup Resolve(TableRequest request)
    {
        foreach (var candidate in request.NameCandidates)
        {
            var matches = images
                .Where(image => string.Equals(image.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    && (request.Schema is null
                        || string.Equals(image.Schema, request.Schema, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 1)
            {
                return new TableLookup { Image = matches[0] };
            }

            if (matches.Count > 1)
            {
                var preferred = matches.SingleOrDefault(image => string.Equals(image.Schema, "dbo", StringComparison.OrdinalIgnoreCase));

                return preferred is not null
                    ? new TableLookup { Image = preferred }
                    : new TableLookup { AmbiguousMatches = [.. matches.Select(image => image.QualifiedName)] };
            }
        }

        return new TableLookup();
    }

    public IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables)
    {
        var referenced = referencedTables
            .Select(t => (t.Schema.ToLowerInvariant(), t.Name.ToLowerInvariant()))
            .ToHashSet();

        return [.. images
            .Where(image => JunctionShape.TryGet(image) is { } shape
                && referenced.Contains((shape.First.ReferencedSchema.ToLowerInvariant(), shape.First.ReferencedTable.ToLowerInvariant()))
                && referenced.Contains((shape.Second.ReferencedSchema.ToLowerInvariant(), shape.Second.ReferencedTable.ToLowerInvariant())))];
    }
}
