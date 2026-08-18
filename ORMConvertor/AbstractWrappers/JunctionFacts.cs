namespace AbstractWrappers;

/// <summary>
/// What the source stated about the junction table behind a many-to-many collection: the
/// table (with schema, where given) and the columns referencing the far side - for
/// NHibernate the table attribute of the collection and the columns of its
/// &lt;many-to-many&gt; element. Builder state next to the pending foreign key columns,
/// not part of the model: the facts wait for the synthesis of the junction entity
/// (decision 005) and live on it from then on.
/// </summary>
/// <param name="Table">Junction table name, or null when the source left it implicit.</param>
/// <param name="Schema">Schema of the junction table, or null.</param>
/// <param name="TargetColumns">Columns of the junction table referencing the target
/// entity's key, in the source's order; null when the source stated none.</param>
public sealed record JunctionFacts(string? Table, string? Schema, IReadOnlyList<string>? TargetColumns);
