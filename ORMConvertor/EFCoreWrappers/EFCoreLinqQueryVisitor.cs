using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqBuilding;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace EFCoreWrappers;

/// <summary>
/// The LINQ visitor over EF Core's provider (decisions 022 and 118): the standard operators
/// of the shared writer, and where the provider has a spelling of its own, EF Core's -
/// <c>EF.Functions.Like</c> for a pattern no string method says exactly (decision 051) and
/// <c>EF.Functions.DateDiff…</c> for a date difference (decision 113). EF Core escapes the
/// argument of a string method itself, so a core the source escaped is the method's argument.
/// </summary>
public sealed class EFCoreLinqQueryVisitor(
    LinqScope scope,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing,
    EFCoreLinqQueryVisitor? outer = null)
    : LinqQueryVisitor(scope, EFCoreLinqMembers.Instance, report, renderSubQuery, typing, outer)
{
    protected override string Provider => "EF Core 10";

    /// <summary>EF.Functions.Like, which EF Core translates to LIKE unchanged, the escape as its third argument (decision 102).</summary>
    protected override string LikeCall(string left, string pattern, string? escape)
        => escape is null
            ? $"EF.Functions.Like({left}, {pattern})"
            : $"EF.Functions.Like({left}, {pattern}, {StringLiteral(escape)})";

    /// <summary>
    /// DATEDIFF as EF.Functions.DateDiffYear through DateDiffSecond, which EF Core 10
    /// translates to DATEDIFF with that unit. Each overload takes two values of one type - a
    /// moment, a moment with an offset, a date, each also nullable - so a date against a moment
    /// has no overload C# can choose and the call does not compile (measured against
    /// Microsoft.EntityFrameworkCore.SqlServer 10.0.10); the query goes out in native SQL. A
    /// side whose scalar this visitor cannot know - a parameter, typed by the gate from the
    /// other side - is no mismatch.
    /// </summary>
    protected override string DateDiff(QueryExpression expression)
    {
        var (start, end) = (expression.Arguments![0], expression.Arguments[1]);
        if (ScalarOf(start) is { } from && ScalarOf(end) is { } to && from != to)
        {
            Report(ConversionRecordKind.Fallback, $"EF.Functions.DateDiff{expression.Unit} has no overload over a {from} and a {to}", QueryFeature.Expression);
            return string.Empty;
        }

        return $"EF.Functions.DateDiff{expression.Unit}({Operand(start)}, {Operand(end)})";
    }
}
