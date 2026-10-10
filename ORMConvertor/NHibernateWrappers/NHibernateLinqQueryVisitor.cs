using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqBuilding;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace NHibernateWrappers;

/// <summary>
/// The LINQ visitor over NHibernate's provider (decision 118): the standard operators of the
/// shared writer, and where the provider spells a thing its own way, NHibernate's. The
/// provider translates the chain into HQL, so it speaks no more than HQL does - the
/// descriptor already keeps a date difference, a set operation, a window and a list aggregate
/// away from it - and a few shapes less, each measured against 5.7.0 by the provider's own
/// query plan (<c>NHibernate/NHibernateLinqProviderTest</c>):
/// <list type="bullet">
/// <item>it joins the argument of StartsWith, EndsWith and Contains with the wildcard as written, so a pattern whose core the source escaped goes through its <c>Like</c> extension (<c>NHibernate.Linq</c>), which takes the escape character beside the pattern;</item>
/// <item>it refuses <c>Count</c> with a predicate over a group of join tuples, and translates <c>Sum</c> over a conditional in its place;</item>
/// <item>it translates COUNT alone over the distinct values of a group;</item>
/// <item>it evaluates a projected expression on the client where C# can, and C# concatenates a null as the empty string where SQL yields NULL (decision 107), so a concatenation projected over text that may hold NULL has no faithful form.</item>
/// </list>
/// </summary>
public sealed class NHibernateLinqQueryVisitor(
    LinqScope scope,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing,
    NHibernateLinqQueryVisitor? outer = null)
    : LinqQueryVisitor(scope, EntityPathMembers.Instance, report, renderSubQuery, typing, outer)
{
    protected override string Provider => "NHibernate 5.7.0";

    /// <summary>
    /// NHibernate's provider concatenates the argument with the wildcard as written (its HQL
    /// generators do not escape), so <c>StartsWith("A_")</c> matches any character after A
    /// there; the reader of NHibernate's LINQ says the same of the source (decision 051).
    /// </summary>
    protected override bool EscapesStringMethodArguments => false;

    /// <summary>
    /// NHibernate 5.7.0 writes the negation inside the NOT EXISTS it makes of All() as SQL's
    /// NOT, which leaves a comparison with a NULL unknown, so All() would keep rows that
    /// SQL's ALL drops; the lambda spells the null tests out instead (decision 119).
    /// </summary>
    protected override bool CompensatesNullSemantics => false;

    /// <summary>The <c>Like</c> extension of <c>NHibernate.Linq</c>, with the escape as a character beside the pattern.</summary>
    protected override string LikeCall(string left, string pattern, string? escape)
        => escape is null
            ? $"{left}.Like({pattern})"
            : $"{left}.Like({pattern}, '{escape}')";

    /// <summary>
    /// HQL of NHibernate 5.7.0 has no date difference (the descriptor leaves DateDiff out of
    /// its functions), so the template never lets one reach this visitor; said here all the
    /// same, for the day the vocabulary of the provider grows.
    /// </summary>
    protected override string DateDiff(QueryExpression expression)
    {
        Report(ConversionRecordKind.Fallback, "The LINQ provider of NHibernate 5.7.0 has no date difference", QueryFeature.Expression);
        return string.Empty;
    }

    /// <summary>
    /// The sum of a conditional over the group - <c>sum(case when x is not null then 1 else 0 end)</c>
    /// in the SQL the provider writes -, which is the count of the non-null values exactly; a
    /// group of GROUP BY is never empty, so the sum is never null. The provider refuses
    /// <c>Count</c> with a predicate over a group whose element is a join tuple.
    /// </summary>
    protected override string CountOfNonNull(string element, string tested)
        => $"{Scope.Param}.Sum({element} => {tested} != null ? 1 : 0)";

    /// <summary>The provider translates <c>Select(…).Distinct().Count()</c> over a group to <c>count(distinct …)</c> and refuses the other aggregates over it.</summary>
    protected override bool AggregatesOverDistinctValues(string function) => function == "COUNT";

    /// <summary>
    /// A concatenation in the final projection over text that may hold NULL: the provider
    /// fetches the columns and lets C# concatenate, where a null becomes the empty string and
    /// SQL, HQL and the HQL form of this query yield NULL (decision 107). In a filter or an
    /// ordering the same expression is translated and compared by the database.
    /// </summary>
    protected override string? Unspoken(QueryExpression expression)
    {
        if (!InProjection
            || !expression.IsBinary
            || expression.Operator is not (ExpressionOperator.Concat or ExpressionOperator.Add)
            || Typing.ScalarOf(expression) is not ScalarType.String)
        {
            return null;
        }

        var nullable = OperandStructure.Inside(expression)
            .FirstOrDefault(leaf => leaf is { IsColumn: true, IsAggregate: false, Property: not null and not "*" } && !HoldsNoNull(leaf.Table, leaf.Property!));

        return nullable is null
            ? null
            : $"The LINQ provider of NHibernate 5.7.0 evaluates a projected concatenation on the client, where C# takes the NULL of '{nullable}' as the empty string and the query yields NULL";
    }
}
