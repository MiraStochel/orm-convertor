using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TransactSql;

/// <summary>
/// A column of a table that a SELECT hands over under a name of its own: <c>a.AuthorId AS Id</c>
/// is the column AuthorId of the table Authors under the name Id.
/// </summary>
/// <param name="Table">The bare name of the table the column belongs to, without its schema.</param>
/// <param name="Column">The column as the text names it.</param>
/// <param name="Name">The name the result gives it.</param>
public sealed record SqlRenamedColumn(string Table, string Column, string Name);

/// <summary>
/// The columns a SELECT renames in its outer projection (decision 131): where a host maps the
/// rows of a query onto a type by the names of the result, the alias is the place a column is
/// mapped to a member. Which host that is and onto which type it maps is the wrapper's to know;
/// what the text says about its columns is read here, where the grammar's types are.
///
/// Only what the text states without doubt: a bare column under an alias that differs from it,
/// in a query specification - a set operation takes its rows from several tables -, of a table
/// the FROM names - an intermediate result or a derived table is no table -, found by its
/// qualifier or, unqualified, as the one table the FROM reads. A name the projection gives
/// twice is left out, because which of the two columns ends up under it depends on the host.
/// </summary>
public static class SqlRenamedColumns
{
    public static IReadOnlyList<SqlRenamedColumn> Of(SqlSelect select)
    {
        ArgumentNullException.ThrowIfNull(select);

        if (Specification(select.Statement.QueryExpression) is not { FromClause: { } from } query)
        {
            return [];
        }

        var definitions = select.Statement.WithCtesAndXmlNamespaces?.CommonTableExpressions
            .Select(expression => expression.ExpressionName.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        // Every source of rows the FROM names, by the name a column qualifies it with; null for
        // a source that is no table. The count is what decides an unqualified column.
        var sources = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var reference in from.TableReferences)
        {
            Collect(reference, definitions, sources, ref count);
        }

        var renamed = new List<SqlRenamedColumn>();

        foreach (var element in query.SelectElements.OfType<SelectScalarExpression>())
        {
            if (element is not
                {
                    ColumnName.Value: { Length: > 0 } name,
                    Expression: ColumnReferenceExpression { ColumnType: ColumnType.Regular, MultiPartIdentifier.Identifiers: { Count: > 0 } parts },
                })
            {
                continue;
            }

            var column = parts[^1].Value;

            if (string.Equals(column, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var table = parts.Count == 1
                ? count == 1 && sources.Count == 1 ? sources.Values.Single() : null
                : sources.GetValueOrDefault(parts[^2].Value);

            if (table is not null)
            {
                renamed.Add(new SqlRenamedColumn(table, column, name));
            }
        }

        // The name every element gives its column of the result: the alias, or the bare
        // column's own name.
        var names = query.SelectElements
            .OfType<SelectScalarExpression>()
            .Select(element => element.ColumnName?.Value
                ?? (element.Expression as ColumnReferenceExpression)?.MultiPartIdentifier?.Identifiers.LastOrDefault()?.Value)
            .OfType<string>()
            .ToList();

        return [.. renamed.Where(column => names.Count(name => string.Equals(name, column.Name, StringComparison.OrdinalIgnoreCase)) == 1)];
    }

    private static QuerySpecification? Specification(QueryExpression? expression) => expression switch
    {
        QuerySpecification query => query,
        QueryParenthesisExpression parenthesis => Specification(parenthesis.QueryExpression),
        _ => null,
    };

    /// <summary>
    /// The sources of rows under one table reference of the FROM, joins walked through. A named
    /// reference to a table is entered under its alias, or under its name where it has none;
    /// any other source - an intermediate result, a derived table, a function - under its alias
    /// with no table, so that a column qualified by it states nothing.
    /// </summary>
    private static void Collect(TableReference reference, HashSet<string> definitions, Dictionary<string, string?> sources, ref int count)
    {
        switch (reference)
        {
            case JoinTableReference join:
                Collect(join.FirstTableReference, definitions, sources, ref count);
                Collect(join.SecondTableReference, definitions, sources, ref count);
                break;

            case JoinParenthesisTableReference parenthesis:
                Collect(parenthesis.Join, definitions, sources, ref count);
                break;

            case NamedTableReference named:
                count++;
                var table = named.SchemaObject.BaseIdentifier.Value;
                var isTable = named.SchemaObject.SchemaIdentifier is not null || !definitions.Contains(table);
                sources[named.Alias?.Value ?? table] = isTable ? table : null;
                break;

            case TableReferenceWithAlias { Alias: { } alias }:
                count++;
                sources[alias.Value] = null;
                break;

            default:
                count++;
                break;
        }
    }
}
