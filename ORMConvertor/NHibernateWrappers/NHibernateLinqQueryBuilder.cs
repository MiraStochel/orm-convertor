using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using LinqBuilding;
using Model;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace NHibernateWrappers;

/// <summary>
/// The second form of an NHibernate query (decision 118): the same instructions as a LINQ
/// chain over <c>session.Query&lt;T&gt;()</c>, written by the shared LINQ writer beside the
/// HQL the <see cref="NHibernateHqlQueryBuilder"/> writes. HQL stays the binding form - the
/// one the levels of verification judge and the Advisor measures -, and this one is offered
/// with it, as a reader of the target would write the query in C#. Its artifact carries a
/// content type of its own, <see cref="ConversionContentType.CSharpLinqQuery"/>, so that a
/// consumer taking "the" method of the query still finds exactly one.
///
/// What is NHibernate's here: the handle and the root, the member a foreign key column is
/// reached through (the path over the reference, as HQL names it), the two join operators
/// the provider of 5.7.0 translates - Join, and GroupJoin with DefaultIfEmpty for a left
/// join; no right or full outer join - and the spellings of <see cref="NHibernateLinqQueryVisitor"/>.
/// The template hands this builder the query only after the HQL form came out in HQL: what
/// HQL does not speak the provider does not translate either, since it translates to HQL.
/// Where this form in turn cannot say the query, the template records that the LINQ form
/// was omitted and the HQL form stands alone.
/// </summary>
public sealed class NHibernateLinqQueryBuilder : AbstractLinqQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    protected override ConversionContentType MethodArtifact => ConversionContentType.CSharpLinqQuery;

    protected override string FormName => "LINQ";

    protected override string Provider => "NHibernate 5.7.0";

    protected override string Root(string entity) => $"session.Query<{entity}>()";

    protected override string Handle => "ISession session";

    protected override string HandleName => "session";

    protected override ILinqMembers Members => EntityPathMembers.Instance;

    /// <summary>
    /// The provider of 5.7.0 keeps an ordering that precedes <c>Distinct()</c> - the HQL it
    /// writes orders the distinct projection - and refuses one that follows it, the opposite
    /// of EF Core 10 (decision 073; measured by the provider's query plan).
    /// </summary>
    protected override bool OrdersAfterDistinct => false;

    protected override LinqQueryVisitor CreateVisitor(
        LinqScope scope,
        Action<ConversionRecordKind, string, QueryFeature?> report,
        Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
        ExpressionTyping typing,
        LinqQueryVisitor? outer)
        => new NHibernateLinqQueryVisitor(scope, report, renderSubQuery, typing, (NHibernateLinqQueryVisitor?)outer);

    /// <summary>
    /// The provider of NHibernate 5.7.0 translates Join, and a left join only in the shape the
    /// compiler makes of <c>join … into g from r in g.DefaultIfEmpty()</c>: a GroupJoin whose
    /// groups a SelectMany flattens with DefaultIfEmpty. The standard LeftJoin and RightJoin
    /// operators of .NET 10 are calls it does not know, and it has no right or full outer join
    /// at all, so those two say that the LINQ form does not speak the join (decision 118).
    /// </summary>
    protected override bool WriteJoin(LinqJoinCall call, QueryArtifact artifact)
    {
        switch (call.Kind)
        {
            case JoinKind.Inner:
                artifact.Joins.Append($"\n        .Join({call.Arguments})");
                return true;

            case JoinKind.Left:
            {
                // The pair of the row of the chain and the group of its matches, under names no
                // row of the chain takes, then the group flattened: a row of the chain without a
                // match yields the pair once, with a null in the joined row's place.
                var group = Fresh(call.RightAlias + "Group", call);
                var pair = Fresh("pair", call);
                var flattened = string.Join(", ", call.LeftMembers.Select(member => $"{pair}.{member}"));

                artifact.Joins.Append(
                    $"\n        .GroupJoin({call.RightSequence}, {call.LeftParam} => {call.LeftKeys}, {call.InnerParam} => {call.RightKeys}, ({call.LeftParam}, {group}) => new {{ {call.LeftParam}, {group} }})" +
                    $"\n        .SelectMany({pair} => {pair}.{group}.DefaultIfEmpty(), ({pair}, {call.RightAlias}) => new {{ {flattened}, {call.RightAlias} }})");
                return true;
            }

            case JoinKind.Right:
            case JoinKind.Full:
                ReportUnspoken(
                    $"The LINQ provider of NHibernate 5.7.0 has no {(call.Kind == JoinKind.Right ? "right" : "full")} outer join - it translates Join, and a left join as GroupJoin with DefaultIfEmpty, and knows neither LeftJoin nor RightJoin",
                    QueryFeature.JoinKind);
                return false;

            default:
                Report(
                    ConversionRecordKind.Failure,
                    $"Join kind {call.Kind} has no LINQ operator; no artifact was generated.",
                    QueryFeature.JoinKind);
                return false;
        }
    }

    /// <summary>A name for a lambda parameter of the join that neither row of the join takes.</summary>
    private string Fresh(string candidate, LinqJoinCall call)
    {
        var name = FreshName(candidate);
        for (var i = 1; name == call.LeftParam || name == call.RightAlias || name == call.InnerParam; i++)
        {
            name = FreshName(candidate + i);
        }

        return name;
    }
}
