using Model.QueryInstructions.Enums;

namespace LinqBuilding;

/// <summary>
/// A join over the equalities of its keys, ready for the provider's operator (decision 118):
/// everything the shared builder derives from the condition and the scope, so that a target
/// writes only the call its provider translates - EF Core 10 the Join, LeftJoin and RightJoin
/// operators and the full join composed from them (decision 065), NHibernate 5.7 the Join and
/// the GroupJoin with a DefaultIfEmpty, which is the one form of a left join its provider
/// knows.
/// </summary>
/// <param name="Kind">The kind of the join.</param>
/// <param name="RightSequence">The joined sequence: the entity's set, filtered by the conjuncts that name the joined row alone (decision 113), or the variable of an intermediate result.</param>
/// <param name="LeftParam">The lambda parameter over the row of the chain so far.</param>
/// <param name="LeftKeys">The key selector's body over <paramref name="LeftParam"/>.</param>
/// <param name="InnerParam">The lambda parameter over the joined row.</param>
/// <param name="RightKeys">The key selector's body over <paramref name="InnerParam"/>.</param>
/// <param name="RightAlias">The alias the joined row takes in the tuple the join yields.</param>
/// <param name="LeftMembers">The rows of the chain as members of the tuple, each written over <paramref name="LeftParam"/>: the row itself before the first join, the members of the tuple after it.</param>
/// <param name="ResultSelector">The result selector that puts the rows of the chain and the joined row side by side, as the two-parameter lambda the Join operator takes.</param>
/// <param name="ChainSoFar">The chain before this join, from its root - what a composed full join repeats.</param>
/// <param name="RootMember">The member of the tuple that holds the root row, which no left join leaves null.</param>
public sealed record LinqJoinCall(
    JoinKind Kind,
    string RightSequence,
    string LeftParam,
    string LeftKeys,
    string InnerParam,
    string RightKeys,
    string RightAlias,
    IReadOnlyList<string> LeftMembers,
    string ResultSelector,
    string ChainSoFar,
    string RootMember)
{
    /// <summary>The arguments of the Join operator and of its outer variants: the sequence, the two key selectors and the result selector.</summary>
    public string Arguments => $"{RightSequence}, {LeftParam} => {LeftKeys}, {InnerParam} => {RightKeys}, {ResultSelector}";
}
