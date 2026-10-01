using Model.QueryInstructions.Conditions;

namespace AbstractWrappers.Descriptors;

/// <summary>
/// The whole vocabulary of <see cref="QueryFunction"/>, for a descriptor that speaks all of
/// it (decision 107), and the vocabulary less what a descriptor leaves out (decision 113).
/// Which functions a descriptor speaks is a fact about its query language, held by a test
/// and not by the type: a seventh framework states the functions it speaks here, and the
/// gate of the builder template sends a query calling the rest to native SQL, or refuses it
/// where the framework has no API for that, without anything in <c>AbstractWrappers</c>
/// changing (S1).
/// </summary>
public static class QueryFunctionVocabulary
{
    public static IReadOnlySet<QueryFunction> All { get; } = Enum.GetValues<QueryFunction>().ToHashSet();

    /// <summary>The vocabulary without the functions named, which the query language does not speak.</summary>
    public static IReadOnlySet<QueryFunction> AllBut(params QueryFunction[] unspoken)
        => All.Except(unspoken).ToHashSet();
}
