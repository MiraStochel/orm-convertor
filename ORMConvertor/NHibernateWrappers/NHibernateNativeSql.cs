using System.Text.RegularExpressions;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using TransactSql;

namespace NHibernateWrappers;

/// <summary>
/// The native SQL NHibernate is handed - the text of an hbm.xml &lt;sql-query&gt; and of
/// <c>ISession.CreateSQLQuery</c> in C# alike (decisions 082 and 113) -, read by the shared
/// T-SQL reader. The wrapper's own part is getting hold of plain T-SQL: refusing a text that
/// carries a placeholder only NHibernate could resolve, and respelling NHibernate's
/// <c>:name</c> as the <c>@name</c> the grammar takes, before the grammar sees the text.
/// </summary>
internal static class NHibernateNativeSql
{
    /// <summary>
    /// The placeholders NHibernate substitutes into a native query before handing it to the
    /// database: {alias}, {alias.property} and {alias.*}. They are not T-SQL and the grammar
    /// must not be taught them (decision 082), so a query that carries one is refused by
    /// name instead of failing as a syntax error at some column.
    /// </summary>
    private static readonly Regex Placeholder = new(
        @"\{[A-Za-z_][A-Za-z0-9_]*(\.([A-Za-z_][A-Za-z0-9_]*|\*))?\}",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads one native query into the builder: the parameters the source binds as lists
    /// (<c>SetParameterList</c>) stated as such, because <c>IN (:ids)</c> is a one-element list
    /// to the grammar otherwise (decision 106). <paramref name="what"/> names the query in the
    /// records - the name of an hbm.xml query, or the call that handed it over.
    /// </summary>
    public static void Read(
        AbstractQueryBuilder builder,
        string sql,
        string what,
        IReadOnlySet<string> lists,
        SourceSqlDialect? declaredSourceDialect,
        ParseLimits limits,
        Action<ConversionRecordKind, string, QueryFeature?> report)
    {
        if (Placeholder.Match(sql) is { Success: true } placeholder)
        {
            report(
                ConversionRecordKind.Failure,
                $"{what} contains the NHibernate placeholder '{placeholder.Value}', which is not T-SQL and which only NHibernate itself could resolve; no artifact was generated.",
                null);
            return;
        }

        if (SqlPlaceholders.FromHost(sql, out var facts, out var unread) is not { } text)
        {
            report(
                ConversionRecordKind.Failure,
                $"{what} writes {unread}, so the query could not be read; no artifact was generated.",
                QueryFeature.QueryParameter);
            return;
        }

        foreach (var list in lists)
        {
            facts[list] = facts.GetValueOrDefault(list) with { IsCollection = true };
        }

        new SqlQueryReader(builder, report, declaredSourceDialect, facts, limits).Read(text);
    }
}
