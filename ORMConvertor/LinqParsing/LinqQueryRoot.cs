using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqParsing;

/// <summary>
/// What a framework's query root names (decision 026). EF Core writes
/// <c>ctx.Customers</c> or <c>ctx.Set&lt;Customer&gt;()</c>, NHibernate writes
/// <c>session.Query&lt;Customer&gt;()</c>; the difference between the wrappers is this
/// record and nothing else.
/// </summary>
/// <param name="Name">
/// The name as the source wrote it — a DbSet name in one framework, an entity type name in
/// the other. Resolving it to a table is the shared parser's job. For an explicit load it is
/// the name of the navigation, since the entity behind it is known only from the mapping.
/// </param>
/// <param name="Load">
/// The explicit load of a navigation the root is, where it is one (decision 115):
/// <c>ctx.Entry(order).Collection(o =&gt; o.Lines).Query()</c>. The provider composes a query
/// out of it - the rows of the navigation's target filtered on the foreign key by the key of
/// the entity in memory -, and the shared parser derives that query from the relation of the
/// mapping. Null for every other root.
/// </param>
public sealed record LinqQueryRoot(string Name, ExplicitLoad? Load = null);

/// <summary>
/// An explicit load of a navigation as the source wrote it (decision 115), recognized by the
/// wrapper in its framework's API and read by the shared parser.
/// </summary>
/// <param name="Owner">The expression the entity in memory is taken from: the argument of <c>Entry(…)</c>.</param>
/// <param name="StatedType">The entity type the call states outright, <c>Entry&lt;SalesOrder&gt;(…)</c>; null where it states none.</param>
/// <param name="Navigation">
/// The navigation the argument names - the member of the lambda's parameter, or the text of
/// the string literal; null where the argument fixes none in the text.
/// </param>
/// <param name="Api">The method of the API that named the navigation, for a record: <c>Collection</c>, <c>Reference</c> or <c>Navigation</c>.</param>
public sealed record ExplicitLoad(ExpressionSyntax Owner, string? StatedType, string? Navigation, string Api)
{
    /// <summary>The load as a record names it: <c>order.Lines</c>, or the API call where no navigation was read.</summary>
    public string Written => Navigation is null ? $"{Owner}.{Api}(…)" : $"{Owner}.{Navigation}";
}
