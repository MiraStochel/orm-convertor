namespace NHibernateWrappers;

/// <summary>
/// How a name is written so that NHibernate's HQL reads it as a name. The grammar of 5.7.0
/// takes a keyword for an identifier only where its parser expects nothing else, and which
/// positions those are was measured on the pinned release with every keyword token of the
/// grammar compiled against a mapping (NHibernate's own query plan, the third level of
/// decision 027):
/// <list type="bullet">
/// <item>an entity named like a keyword - <c>Order</c>, <c>Group</c>, <c>Select</c> - is refused as the target of an entity join, and seven of the keywords even at the head of a from clause, a subquery's included; the name qualified with its namespace (<c>Shop.Order</c>) was read in every position;</item>
/// <item>an alias spelled like a keyword is refused nearly wherever it stands - <c>order</c> and <c>group</c> only in a distinct projection, inside <c>count()</c> and after <c>join</c>, the others everywhere -, while the same alias with a trailing underscore was read in every position.</item>
/// </list>
/// A projection alias was read as a keyword everywhere but for <c>ascending</c> and
/// <c>descending</c>, and is left as the source wrote it.
/// </summary>
internal static class HqlNames
{
    /// <summary>The keywords of the HQL grammar of NHibernate 5.7.0 (the lexical tokens of its parser and the literals asc, desc and by), compared without regard to case as HQL compares them.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "and", "any", "as", "asc", "ascending", "avg", "between", "both", "by", "case",
        "class", "count", "cross", "delete", "desc", "descending", "distinct", "elements", "else",
        "empty", "end", "escape", "exists", "false", "fetch", "from", "full", "group", "having",
        "in", "indices", "inner", "insert", "into", "is", "join", "leading", "left", "like", "max",
        "member", "min", "new", "not", "null", "object", "of", "on", "or", "order", "outer",
        "properties", "right", "select", "set", "skip", "some", "sum", "take", "then", "trailing",
        "true", "union", "update", "versioned", "when", "where", "with",
    };

    /// <summary>The keywords the parser refuses as an entity name even at the head of a from clause.</summary>
    private static readonly HashSet<string> NeverNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ascending", "cross", "descending", "left", "right", "skip", "take",
    };

    /// <summary>
    /// The alias as HQL reads it: a keyword gets a trailing underscore. Applied wherever the
    /// alias is written - its declaration and every path over it alike - so the query names
    /// one row as the source did.
    /// </summary>
    public static string Alias(string alias) => Keywords.Contains(alias) ? alias + "_" : alias;

    /// <summary>
    /// The entity name as HQL reads it as the target of an entity join (<paramref name="joined"/>)
    /// or at the head of a from clause: the bare name where NHibernate reads it - every name
    /// that is no keyword stays exactly as it was -, otherwise the name qualified with the
    /// namespace the generated mapping declares on &lt;hibernate-mapping&gt;; null where
    /// NHibernate refuses the bare name and the entity has no namespace to qualify it with.
    /// </summary>
    public static string? Entity(string name, string? entityNamespace, bool joined)
    {
        if (!Keywords.Contains(name) || !(joined || NeverNames.Contains(name)))
        {
            return name;
        }

        return string.IsNullOrEmpty(entityNamespace) ? null : $"{entityNamespace}.{name}";
    }
}
