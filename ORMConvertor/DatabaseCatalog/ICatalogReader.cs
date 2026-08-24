namespace DatabaseCatalog;

/// <summary>
/// One request for the image of one table. The caller states its policy through the
/// candidates: a source-stated table name is a single exact candidate, an entity without a
/// stated table offers its name and the plural/singular variant (see
/// <see cref="Common.Naming.EntityTableNaming"/>, decision 050). Candidates are tried in
/// order and the first one with a match wins.
/// </summary>
/// <param name="Key">Key the result is returned under, typically the entity name.</param>
/// <param name="Schema">Schema the source stated, or null when any schema may match.</param>
/// <param name="NameCandidates">Table names to try, in priority order.</param>
public sealed record TableRequest(string Key, string? Schema, IReadOnlyList<string> NameCandidates);

/// <summary>
/// Result of one <see cref="TableRequest"/>: the resolved image, or the qualified names
/// that matched when the request could not be resolved to a single table.
/// </summary>
public sealed class TableLookup
{
    public TableImage? Image { get; init; }

    /// <summary>Qualified names of the tables that matched when the match was ambiguous.</summary>
    public IReadOnlyList<string> AmbiguousMatches { get; init; } = [];
}

/// <summary>
/// The one place that reads database metadata (decision 015). The mechanism - connection,
/// dialect, the concrete catalog queries, batching - lives behind this interface; what gets
/// written into the intermediate representation is decided by the consumer, not here.
/// </summary>
public interface ICatalogReader
{
    /// <summary>
    /// Reads the images of the requested tables in one batch and resolves each request to
    /// at most one of them. Throws when the catalog cannot be reached - the caller decides
    /// whether that is fatal (a benchmark run) or a diagnostic record (a translation).
    /// </summary>
    IReadOnlyDictionary<string, TableLookup> ReadTables(IReadOnlyList<TableRequest> requests);

    /// <summary>
    /// Full images of the junction-shaped tables (see <see cref="JunctionShape"/>) whose
    /// two key-forming foreign keys both point at the given tables. This is how a
    /// many-to-many nobody's artifact expresses is found: the junction table exists only
    /// in the schema, so only the catalog can name it (decisions 005 and 015).
    /// </summary>
    IReadOnlyList<TableImage> FindJunctionTables(IReadOnlyCollection<TableImage> referencedTables);
}
