namespace LinqParsing;

/// <summary>
/// What a framework's query root names (decision 026). EF Core writes
/// <c>ctx.Customers</c> or <c>ctx.Set&lt;Customer&gt;()</c>, NHibernate writes
/// <c>session.Query&lt;Customer&gt;()</c>; the difference between the wrappers is this
/// record and nothing else.
/// </summary>
/// <param name="Name">
/// The name as the source wrote it — a DbSet name in one framework, an entity type name in
/// the other. Resolving it to a table is the shared parser's job.
/// </param>
public sealed record LinqQueryRoot(string Name);
