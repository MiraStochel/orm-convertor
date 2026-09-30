using Model.QueryInstructions.Conditions;

namespace AbstractWrappers.Descriptors;

/// <summary>
/// The whole vocabulary of <see cref="QueryFunction"/>, for a descriptor that speaks all of
/// it (decision 107). That all six descriptors do today is a fact about today, held by a
/// test and not by the type: a seventh framework states the functions it speaks here, and
/// the gate of the builder template refuses the rest by name without anything in
/// <c>AbstractWrappers</c> changing (S1).
/// </summary>
public static class QueryFunctionVocabulary
{
    public static IReadOnlySet<QueryFunction> All { get; } = Enum.GetValues<QueryFunction>().ToHashSet();
}
