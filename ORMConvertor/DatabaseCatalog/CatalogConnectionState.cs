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
/// What the completion phase reports about itself: the state of the catalog connection and
/// how long the read took - null when the connection was never tried, so the duration
/// cannot claim a read that did not happen (S3).
/// </summary>
public sealed record CatalogPhaseResult(CatalogConnectionState ConnectionState, TimeSpan? ReadTime);
