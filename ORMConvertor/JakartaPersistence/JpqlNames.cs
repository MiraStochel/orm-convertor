namespace JakartaPersistence;

/// <summary>
/// How an alias - an identification variable - is written so that both implementations
/// read it. The specification forbids its reserved identifiers as identification variables,
/// and the pinned releases hold to it unevenly: measured with an alias in every position a
/// query writes one (declaration, path, select, distinct, count, ordering, entity join, path
/// join, subquery), EclipseLink 5.0.0 refuses <c>select</c>, <c>count</c>, <c>value</c>,
/// <c>key</c>, <c>size</c>, <c>member</c>, <c>object</c>, <c>type</c> and <c>index</c> wherever
/// they stand and Hibernate 7.4.5 <c>left</c>, while both read the same alias with a trailing
/// underscore in every position. A reserved identifier gets that underscore, in both
/// implementations alike, wherever the alias is written, so the query still names one row
/// as the source did; every other alias stays exactly as it was. An entity name cannot be
/// respelled the same way - JPQL has no qualified or quoted entity name -, so what a parser
/// refuses there goes to native SQL (<see cref="JpaImplementationProfile.EntityNamesRefused"/>).
/// </summary>
internal static class JpqlNames
{
    /// <summary>The identifiers of the JPQL grammar EclipseLink 5.0.0 parses (its expression registry), which include those the specification reserves; compared without regard to case.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "abs", "all", "and", "any", "as", "asc", "avg", "between", "bit_length", "both", "by", "case",
        "cast", "ceiling", "char_length", "character_length", "class", "coalesce", "column", "concat",
        "connect", "count", "current_date", "current_time", "current_timestamp", "date", "datetime",
        "delete", "desc", "distinct", "else", "empty", "end", "entry", "escape", "except", "exists",
        "exp", "extract", "false", "fetch", "first", "floor", "from", "func", "function", "group",
        "having", "id", "in", "index", "inner", "intersect", "is", "join", "key", "last", "leading",
        "left", "length", "like", "ln", "local", "locate", "lower", "max", "member", "min", "mod",
        "new", "not", "null", "nullif", "nulls", "object", "of", "on", "operator", "or", "order",
        "outer", "position", "power", "regexp", "replace", "right", "round", "scn", "select", "set",
        "siblings", "sign", "size", "some", "sql", "sqrt", "start", "substring", "sum", "table",
        "then", "time", "timestamp", "trailing", "treat", "trim", "true", "type", "union", "unknown",
        "update", "upper", "value", "version", "when", "where", "with",
    };

    /// <summary>The alias as JPQL reads it: a reserved identifier gets a trailing underscore.</summary>
    public static string Alias(string alias) => Reserved.Contains(alias) ? alias + "_" : alias;
}
