using System.Text.RegularExpressions;

namespace Tests.Differential;

/// <summary>
/// The deliberately wrong translations F13 asks to be detected (decision 089). Each one is
/// applied to the <em>generated artifact</em> and not to the translator: mutating the
/// translator would be a tool of its own, with bugs of its own, and most of its mutations
/// would produce artifacts that do not even compile - which proves nothing about comparing
/// results. What is under test here is the sensitivity of the comparison, so the mutation
/// has to reach the rows.
///
/// The list is the one the decision names, and the fixture of that decision is chosen so
/// that every one of them changes the outcome over it. A mutation that leaves the artifact
/// unchanged is itself a failure: it would pass by accident.
///
/// The transformations are textual and language-blind on purpose. The artifact of every
/// target puts the filter, the ordering and the projection where a line or a comma says
/// they are, so one rule reaches SQL, HQL, JPQL and a LINQ chain alike.
/// </summary>
internal sealed record DifferentialMutation(string Key, string Name, Func<string, string> Apply)
{
    public static IEnumerable<DifferentialMutation> All() =>
    [
        new("filter", "the filter is dropped", DropFilter),
        new("operator", "the comparison operator is flipped", FlipOperator),
        new("ordering", "the ordering is dropped", DropOrdering),
        new("rowCount", "the row count is changed", ChangeRowCount),
        new("projection", "two projected fields are swapped", SwapProjection),
    ];

    /// <summary>The mutation a matrix entry names, or a failure that says the name is not one.</summary>
    public static DifferentialMutation Of(string key)
        => All().SingleOrDefault(mutation => mutation.Key == key)
           ?? throw new InvalidOperationException(
               $"matrix.txt names the mutation \"{key}\", which is none of "
               + string.Join(", ", All().Select(mutation => mutation.Key)) + ".");

    /// <summary>Every target writes its filter on a line of its own - WHERE, where, .Where(.</summary>
    private static string DropFilter(string artifact)
        => Lines(artifact, line => !Regex.IsMatch(line, @"^\s*(WHERE|where)\b") && !line.Contains(".Where("));

    /// <summary>
    /// The comparison the filter is built on. Over this fixture the two directions select
    /// disjoint, non-empty sets, so the flip cannot go unnoticed.
    /// </summary>
    private static string FlipOperator(string artifact)
    {
        // Whitespace on both sides, which is what tells a comparison from the angle
        // brackets of List&lt;Product&gt; and from the arrow of a lambda. Without it the
        // mutation rewrites the generics of the artifact and the thing does not compile -
        // which would prove that a broken artifact differs, not that a wrong query does.
        var flipped = Regex.Replace(artifact, @"(?<=\s)>(?=\s)", "\u0001");
        flipped = Regex.Replace(flipped, @"(?<=\s)<(?=\s)", ">");
        return flipped.Replace('\u0001', '<');
    }

    /// <summary>
    /// Ordering lives on its own line as well - ORDER BY, order by, .OrderBy. The last one
    /// without its parenthesis on purpose: a descending ordering reaches the EF Core
    /// artifact as .OrderByDescending, and a rule that missed it would leave the artifact
    /// untouched for every query that orders the other way.
    /// </summary>
    private static string DropOrdering(string artifact)
        => Lines(artifact, line => !Regex.IsMatch(line, @"(ORDER\s+BY|order\s+by)\b") && !line.Contains(".OrderBy"));

    /// <summary>
    /// The row count of a paginated query, wherever the target put it: inside TOP, in a
    /// Take call, on the query object. Three of six rows is a proper prefix, so two is a
    /// different answer whichever way the target slices.
    /// </summary>
    private static string ChangeRowCount(string artifact)
        => Regex.Replace(artifact, @"(TOP\s*\(\s*|\.Take\(|SetMaxResults\(|setMaxResults\()3(\s*\))", "${1}2${2}");

    /// <summary>
    /// The two projected expressions swapped under their own aliases: the row keeps its
    /// shape and every field holds the other one's value. Over this fixture Sku and
    /// ProductName differ in every row, so nothing survives the swap by coincidence.
    /// </summary>
    private static string SwapProjection(string artifact)
    {
        // SELECT p.A AS X, p.B AS Y  - the shape of SQL, HQL and JPQL.
        var swapped = Regex.Replace(
            artifact,
            @"(\w+\.)(\w+)(\s+(?:AS|as)\s+)(\w+)(,\s*)(\w+\.)(\w+)(\s+(?:AS|as)\s+)(\w+)",
            "$1$7$3$4$5$6$2$8$9");

        if (swapped != artifact)
        {
            return swapped;
        }

        // new { X = p.A, Y = p.B } - the shape of a LINQ projection.
        return Regex.Replace(
            artifact,
            @"(\w+)(\s*=\s*)(\w+\.)(\w+)(,\s*)(\w+)(\s*=\s*)(\w+\.)(\w+)",
            "$1$2$3$9$5$6$7$8$4");
    }

    /// <summary>
    /// The artifact without the lines a rule rejects - and the artifact itself when it
    /// rejects none. Returning a rebuilt text either way would make every no-op look like a
    /// mutation, because rebuilding normalizes the line endings; a query with no ordering
    /// would then be reported as having lost one.
    /// </summary>
    private static string Lines(string artifact, Func<string, bool> keep)
    {
        var lines = artifact.Replace("\r\n", "\n").Split('\n');
        var kept = lines.Where(keep).ToList();

        return kept.Count == lines.Length ? artifact : string.Join(Environment.NewLine, kept);
    }
}
