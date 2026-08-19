using System.Text;

namespace AbstractWrappers;

/// <summary>
/// The query under construction, one slot per clause (decision 023). The query-branch
/// counterpart of the Code/Mapping pair the entity branch keeps on its artifact.
///
/// Slots exist so that the order in which the steps <em>run</em> can differ from the order
/// in which the text is <em>assembled</em>. The steps run in relational evaluation order,
/// which is what LINQ writes literally; SQL and HQL then move the projection to the front
/// when the final step joins the slots up. Without that separation one template could not
/// serve both.
/// </summary>
public sealed class QueryArtifact
{
    public StringBuilder Source { get; } = new();

    public StringBuilder Joins { get; } = new();

    public StringBuilder Filter { get; } = new();

    public StringBuilder Grouping { get; } = new();

    public StringBuilder PostFilter { get; } = new();

    public StringBuilder Ordering { get; } = new();

    public StringBuilder Projection { get; } = new();

    /// <summary>
    /// Name of the entity the query materialises, as resolved by the source step. Every
    /// target needs it for the same reason — Dapper writes Query&lt;T&gt;, EF Core
    /// IQueryable&lt;T&gt;, NHibernate CreateQuery&lt;T&gt; — so it belongs to the artifact
    /// rather than to one builder's private state.
    /// </summary>
    public string? ResultEntity { get; set; }
}
