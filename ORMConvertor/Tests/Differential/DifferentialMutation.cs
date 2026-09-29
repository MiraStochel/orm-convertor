using System.Globalization;
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

    /// <summary>
    /// Every target writes its filter on a line of its own - WHERE, where, .Where(. A HAVING
    /// is not a filter in this sense and stays, so a query whose only condition is a HAVING
    /// carries the operator mutation and not this one.
    /// </summary>
    private static string DropFilter(string artifact)
        => Lines(artifact, line => !Regex.IsMatch(line, @"^\s*(WHERE|where)\b") && !line.Contains(".Where("));

    /// <summary>
    /// The comparison the filter is built on: greater against less, and greater-or-equal
    /// against less-or-equal. Over the fixture the two directions of every comparison select
    /// different sets, so the flip cannot go unnoticed.
    /// </summary>
    private static string FlipOperator(string artifact)
    {
        // Whitespace on both sides, which is what tells a comparison from the angle
        // brackets of List<Product>, from the arrow of a lambda and from the equals of an
        // assignment. Without it the mutation rewrites the generics of the artifact and the
        // thing does not compile - which would prove that a broken artifact differs, not
        // that a wrong query does. The bounded pair goes first, because ">=" ends in a
        // character the bare rule would otherwise not see as whitespace-bounded anyway.
        var flipped = Regex.Replace(artifact, @"(?<=\s)>=(?=\s)", "\u0003");
        flipped = Regex.Replace(flipped, @"(?<=\s)<=(?=\s)", ">=");
        flipped = flipped.Replace("\u0003", "<=");

        flipped = Regex.Replace(flipped, @"(?<=\s)>(?=\s)", "\u0001");
        flipped = Regex.Replace(flipped, @"(?<=\s)<(?=\s)", ">");
        return flipped.Replace('\u0001', '<');
    }

    /// <summary>
    /// Ordering lives on its own line as well - ORDER BY, order by, .OrderBy and the .ThenBy
    /// a second key continues it with. The LINQ ones without their parenthesis on purpose: a
    /// descending ordering reaches the EF Core artifact as .OrderByDescending, and a rule that
    /// missed it would leave the artifact untouched for every query that orders the other way.
    /// </summary>
    private static string DropOrdering(string artifact)
        => Lines(artifact, line => !Regex.IsMatch(line, @"(ORDER\s+BY|order\s+by)\b")
            && !line.Contains(".OrderBy")
            && !line.Contains(".ThenBy"));

    /// <summary>
    /// The row count of a paginated query, wherever the target put it: inside TOP, after
    /// FETCH NEXT, in a Take call, on the query object. A literal count becomes one less, a
    /// bound one - a parameter of the generated method (decision 085) - becomes itself plus
    /// one, an expression every one of those places accepts; either way the slice is another
    /// slice of the same ordered rows.
    /// </summary>
    private static string ChangeRowCount(string artifact)
    {
        const string places = @"(TOP\s*\(\s*|\.Take\(|SetMaxResults\(|setMaxResults\(|FETCH NEXT )";

        var changed = Regex.Replace(
            artifact,
            places + @"(\d+)(\s*\)| ROWS)",
            match => match.Groups[1].Value
                + (int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture)
                + match.Groups[3].Value);

        return Regex.Replace(changed, places + @"(@?[A-Za-z_]\w*|#\{\w+\})(\s*\)| ROWS)", "$1$2 + 1$3");
    }

    /// <summary>
    /// The two projected expressions swapped under their own aliases: the row keeps its
    /// shape and every field holds the other one's value. Over the fixture no two projected
    /// fields of a query agree in every row, so nothing survives the swap by coincidence.
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

        // new { X = p.A, Y = t.p.B } - the shape of a LINQ projection, whose path may run
        // through the joined row of a result selector (decision 103); the last member is what
        // is swapped, the path in front of it stays.
        return Regex.Replace(
            artifact,
            @"(\w+)(\s*=\s*)((?:\w+\.)+)(\w+)(,\s*)(\w+)(\s*=\s*)((?:\w+\.)+)(\w+)",
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
