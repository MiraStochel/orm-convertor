using System.Text;

namespace Common.Naming;

/// <summary>
/// The name of the method a translated query is emitted as (decision 081). A source that
/// names its queries writes the name in its own vocabulary - <c>&lt;query name="findById"&gt;</c>,
/// <c>@NamedQuery(name = "Customer.findAll")</c>, <c>&lt;select id="find-by-id"&gt;</c> - and the
/// target's language decides how that name is spelled: PascalCase for the .NET targets,
/// camelCase for the Java ones. A query the source did not name keeps the fixed fallback,
/// which can never collide with itself: a unit carrying more than one query is exactly the
/// unit whose queries the source named.
///
/// Lives in Common for the same reason <see cref="EntityTableNaming"/> does: every builder
/// needs it and none may reach into another's project (S1).
/// </summary>
public static class QueryMethodNaming
{
    /// <summary>The method name for a .NET target, where methods are PascalCase.</summary>
    public static string PascalCase(string? queryName, string fallback)
        => Spell(queryName, fallback, firstUpper: true);

    /// <summary>The method name for a Java target, where methods are camelCase.</summary>
    public static string CamelCase(string? queryName, string fallback)
        => Spell(queryName, fallback, firstUpper: false);

    /// <summary>
    /// Words of the source name joined into one identifier. Anything that cannot stand in
    /// an identifier separates words rather than vanishing, so "Customer.findAll" comes out
    /// as CustomerFindAll and not as CustomerfindAll, and a leading digit is separated from
    /// the front the same way, because an identifier may not begin with one. Word breaks
    /// the source wrote in case - findById - are kept, so a name already spelled as an
    /// identifier comes back as itself.
    ///
    /// A name that yields no identifier character at all falls back to the fixed name: an
    /// empty method name is not an answer. A name that collides with a keyword of the
    /// target language is not renamed, the same as an entity or a property name, whose
    /// legality the tool takes from the source.
    /// </summary>
    private static string Spell(string? queryName, string fallback, bool firstUpper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        if (string.IsNullOrWhiteSpace(queryName))
        {
            return fallback;
        }

        var spelled = new StringBuilder();
        var upperNext = firstUpper;

        foreach (var character in queryName)
        {
            if (char.IsLetter(character) || (character == '_' && spelled.Length > 0))
            {
                spelled.Append(upperNext ? char.ToUpperInvariant(character) : character);
                upperNext = false;
                continue;
            }

            // A digit may follow an identifier character but never open one, so before the
            // first letter it counts as a separator like any other.
            if (char.IsDigit(character) && spelled.Length > 0)
            {
                spelled.Append(character);
                continue;
            }

            upperNext = true;
        }

        if (spelled.Length == 0)
        {
            return fallback;
        }

        if (!firstUpper)
        {
            spelled[0] = char.ToLowerInvariant(spelled[0]);
        }

        return spelled.ToString();
    }
}
