namespace AbstractWrappers;

/// <summary>
/// One table a query needs bound to an entity of the conversion before it can be built, as
/// the query names it (decision 105). The scalar of a parameter follows from the column on
/// the other side of its comparison (decision 083); the column's qualifier leads to a table,
/// and the table leads to an entity only through a stated mapping - the table the mapping
/// names, or an entity named like the table - never through the naming convention of
/// decision 050, because a scalar in the signature of a generated method is a claim about
/// the caller and may follow from a fact only. A source that names tables and states no
/// mapping for them, Dapper, is the one source that leaves such a qualifier unresolved, and
/// the catalog is where the binding comes from.
///
/// The demand is data, not a call (decision 015): the template yields it once the query is
/// read, the orchestration hands it to the catalog component before <see cref="AbstractQueryBuilder.Build"/>,
/// and nothing in a builder touches a database.
/// </summary>
/// <param name="Schema">The schema the query wrote before the table, or null where it wrote none.</param>
/// <param name="Table">The bare table name, as the query wrote it.</param>
public sealed record QueryTableDemand(string? Schema, string Table)
{
    /// <summary>The name as the query wrote it, schema included where it wrote one.</summary>
    public string QualifiedName => Schema is null ? Table : $"{Schema}.{Table}";

    /// <summary>
    /// The demand for a table as a FROM or JOIN names it - bare, or qualified with its
    /// schema. A three-part name keeps its last two parts: the database is the
    /// connection's, not the query's, and the catalog reader reads one database.
    /// </summary>
    public static QueryTableDemand Of(string tableAsWritten)
    {
        ArgumentNullException.ThrowIfNull(tableAsWritten);

        var parts = tableAsWritten.Split('.');

        return parts.Length >= 2
            ? new QueryTableDemand(parts[^2], parts[^1])
            : new QueryTableDemand(null, tableAsWritten);
    }

    /// <summary>
    /// Whether two demands name the same table: the same schema (or both none) and the
    /// same name, compared the way the catalog compares identifiers - without regard to case.
    /// </summary>
    public bool Names(QueryTableDemand other)
        => string.Equals(QualifiedName, other.QualifiedName, StringComparison.OrdinalIgnoreCase);
}
