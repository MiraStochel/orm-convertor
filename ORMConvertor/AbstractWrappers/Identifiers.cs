using System.Text.RegularExpressions;

namespace AbstractWrappers;

/// <summary>
/// The shape of a name every target language reads as a name of its own: a letter or an
/// underscore, then letters, digits and underscores. A name a source had to delimit
/// (<c>"product.productId"</c>) and that does not fit it is spelled delimited by a SQL
/// target and has no member name in C#, no result variable in JPQL or HQL.
/// </summary>
public static class Identifiers
{
    private static readonly Regex Regular = new(@"^[\p{L}_][\p{L}\p{Nd}_]*$", RegexOptions.CultureInvariant);

    public static bool IsRegular(string name) => Regular.IsMatch(name);
}
