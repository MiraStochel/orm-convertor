using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqBuilding;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Emits a LINQ method chain, which is EF Core's own query form (decision 022), over the
/// shared LINQ writer (decision 118). The artifact is a method taking a <c>DbContext</c> and
/// returning <c>IQueryable</c>, with the root written as <c>ctx.Set&lt;T&gt;()</c> (decision
/// 027). What is EF Core's here: the member a foreign key column is reached through
/// (<see cref="EFCoreColumnMember"/>), the LeftJoin and RightJoin operators of EF Core 10 and
/// the full join composed from them (decision 065), and the spellings of its provider
/// (<see cref="EFCoreLinqQueryVisitor"/>).
/// </summary>
public class EFCoreLinqQueryBuilder : AbstractLinqQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    /// <summary>What LINQ does not speak goes out as native SQL through SqlQuery or FromSql (decision 113).</summary>
    protected override AbstractQueryBuilder NativeSqlBuilder() => new EFCoreNativeSqlQueryBuilder();

    protected override string Provider => "EF Core 10";

    protected override string Root(string entity) => $"ctx.Set<{entity}>()";

    protected override string Handle => "DbContext ctx";

    protected override string HandleName => "ctx";

    protected override ILinqMembers Members => EFCoreLinqMembers.Instance;

    protected override LinqQueryVisitor CreateVisitor(
        LinqScope scope,
        Action<ConversionRecordKind, string, QueryFeature?> report,
        Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
        ExpressionTyping typing,
        LinqQueryVisitor? outer)
        => new EFCoreLinqQueryVisitor(scope, report, renderSubQuery, typing, (EFCoreLinqQueryVisitor?)outer);

    protected override bool WriteJoin(LinqJoinCall call, QueryArtifact artifact)
    {
        if (call.Kind == JoinKind.Full)
        {
            // EF Core 10 has LeftJoin and RightJoin but no full outer join, and an inner
            // join in its place would return different rows (decision 065). The full join
            // is composed from the operators that do exist: the left join's rows,
            // concatenated with the right join's rows that found no left match. Concat is
            // UNION ALL, so matched pairs are not doubled - the filter excludes them from
            // the right branch - and genuine duplicates are not collapsed the way Union
            // would. The filter stands on the root member because that one is never null
            // in the left branch. A faithful translation is neither a loss nor a
            // convention, so no record is issued.
            var probe = FreshName("x");

            artifact.Joins.Append(
                $"\n        .LeftJoin({call.Arguments})" +
                $"\n        .Concat({call.ChainSoFar}" +
                $"\n            .RightJoin({call.Arguments})" +
                $"\n            .Where({probe} => {probe}.{call.RootMember} == null))");
            return true;
        }

        var method = call.Kind switch
        {
            JoinKind.Inner => "Join",
            JoinKind.Left => "LeftJoin",
            JoinKind.Right => "RightJoin",
            _ => null,
        };

        if (method is null)
        {
            // No catch-all translation: a JoinKind value without an operator must not
            // come out as a neighbouring join (decision 053).
            Report(
                ConversionRecordKind.Failure,
                $"Join kind {call.Kind} has no LINQ operator; no artifact was generated.",
                QueryFeature.JoinKind);
            return false;
        }

        artifact.Joins.Append($"\n        .{method}({call.Arguments})");
        return true;
    }
}

/// <summary>The members EF Core's LINQ names, through <see cref="EFCoreColumnMember"/> (decision 012).</summary>
internal sealed class EFCoreLinqMembers : ILinqMembers
{
    public static EFCoreLinqMembers Instance { get; } = new();

    public string? PathOf(Model.AbstractRepresentation.EntityMap? map, string column) => EFCoreColumnMember.PathOf(map, column);

    public Model.AbstractRepresentation.LangType? TypeOf(Model.AbstractRepresentation.EntityMap? map, string column) => EFCoreColumnMember.TypeOf(map, column);

    public (Model.AbstractRepresentation.Enums.ScalarType Scalar, bool Nullable)? DeclaredValue(Model.AbstractRepresentation.EntityMap? map, string column)
        => EFCoreColumnMember.DeclaredValue(map, column);
}
