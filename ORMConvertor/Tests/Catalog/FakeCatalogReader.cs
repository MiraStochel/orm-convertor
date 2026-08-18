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
            var match = request.NameCandidates
                .Select(candidate => images.FirstOrDefault(image =>
                    string.Equals(image.Name, candidate, StringComparison.OrdinalIgnoreCase)
                    && (request.Schema is null
                        || string.Equals(image.Schema, request.Schema, StringComparison.OrdinalIgnoreCase))))
                .FirstOrDefault(image => image is not null);

            results[request.Key] = new TableLookup { Image = match };
        }

        return results;
    }
}
