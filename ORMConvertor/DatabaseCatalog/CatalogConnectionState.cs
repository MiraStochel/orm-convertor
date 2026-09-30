namespace DatabaseCatalog;

/// <summary>
/// State of the catalog connection during one completion phase. The connection lives in
/// server configuration and the interface only shows its state (decision 030), so this is
/// the one first-class answer to "did the catalog take part in this translation" - the
/// diagnostic records carry the same fact, but only as one entry among many.
/// </summary>
public enum CatalogConnectionState
{
    /// <summary>No connection string is configured; the translation ran on conventions.</summary>
    NotConfigured,

    /// <summary>A connection is configured, but the phase had nothing to ask - an empty
    /// demand or no named entities - so the connection was never tried.</summary>
    Unused,

    /// <summary>The catalog was read.</summary>
    Reached,

    /// <summary>A connection is configured but the read failed; the translation continued
    /// on conventions and a record says why.</summary>
    Unreachable,
}

/// <summary>
/// What a completion phase reports about itself: the state of the catalog connection and
/// how long the read took - null when the connection was never tried, so the duration
/// cannot claim a read that did not happen (S3).
/// </summary>
public sealed record CatalogPhaseResult(CatalogConnectionState ConnectionState, TimeSpan? ReadTime)
{
    /// <summary>
    /// This phase followed by a later one over the same connection - the target's demand
    /// before the entities are built, the queries' demand before the queries are
    /// (decision 105). One run reports one state and one time: the state is the strongest
    /// thing that happened to the connection - a failed read outranks a successful one, a
    /// read outranks a connection nothing asked - and the time is the sum of the reads,
    /// null where neither phase tried the connection (S3).
    /// </summary>
    public CatalogPhaseResult Then(CatalogPhaseResult later)
    {
        ArgumentNullException.ThrowIfNull(later);

        var state = (ConnectionState, later.ConnectionState) switch
        {
            (CatalogConnectionState.Unreachable, _) or (_, CatalogConnectionState.Unreachable) => CatalogConnectionState.Unreachable,
            (CatalogConnectionState.Reached, _) or (_, CatalogConnectionState.Reached) => CatalogConnectionState.Reached,
            (CatalogConnectionState.Unused, _) or (_, CatalogConnectionState.Unused) => CatalogConnectionState.Unused,
            _ => CatalogConnectionState.NotConfigured,
        };

        TimeSpan? time = (ReadTime, later.ReadTime) switch
        {
            (null, null) => null,
            (null, var only) => only,
            (var only, null) => only,
            (var first, var second) => first + second,
        };

        return new CatalogPhaseResult(state, time);
    }
}
