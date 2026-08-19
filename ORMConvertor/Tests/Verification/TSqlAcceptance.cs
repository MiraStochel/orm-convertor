using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model.AbstractRepresentation;

namespace Tests.Verification;

/// <summary>
/// Verification of a Dapper query (decision 027). Dapper is a materialiser, not a query
/// engine: it accepts any string, so "the framework accepts it" would be an empty claim and
/// the third level collapses into the second. What can be said is stated here instead — the
/// SQL parses, and every table and column it names is resolvable through the mapping IR,
/// which is rule Q13 checked by us because no framework will do it for us.
/// </summary>
internal static class TSqlAcceptance
{
    /// <summary>Parses the SQL and fails the test with line and column if it does not.</summary>
    public static TSqlFragment ParseOrFail(string sql)
    {
        var fragment = new TSql160Parser(initialQuotedIdentifiers: true)
            .Parse(new StringReader(sql), out var errors);

        Assert.True(errors.Count == 0, "Generated SQL does not parse:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, errors.Select(e => $"({e.Line},{e.Column}) {e.Message}")));

        return fragment;
    }

    /// <summary>
    /// Every table the SQL names has to be a table of the conversion, and every qualified
    /// column has to belong to one of them. An alias is resolved back to its table first.
    /// </summary>
    public static void ResolvesAgainst(string sql, IReadOnlyList<EntityMap> entityMaps)
    {
        var fragment = ParseOrFail(sql);

        var tables = new TableCollector();
        fragment.Accept(tables);

        foreach (var (name, _) in tables.Tables)
        {
            Assert.True(
                entityMaps.Any(m => Matches(m, name)),
                $"The SQL names table '{name}', which no entity of the conversion maps to.");
        }

        var columns = new ColumnCollector();
        fragment.Accept(columns);

        foreach (var (qualifier, column) in columns.Columns)
        {
            if (qualifier is null)
            {
                continue;
            }

            var map = entityMaps.FirstOrDefault(m => Matches(m, tables.TableFor(qualifier) ?? qualifier));
            if (map is null)
            {
                continue;
            }

            Assert.True(
                map.PropertyMaps.Any(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase)),
                $"The SQL names column '{qualifier}.{column}', which entity '{map.Entity.Name}' does not carry.");
        }
    }

    private static bool Matches(EntityMap map, string name)
    {
        var bare = name.Split('.').LastOrDefault() ?? name;

        return string.Equals($"{map.Schema}.{map.Table}", name, StringComparison.OrdinalIgnoreCase)
               || string.Equals(map.Table, bare, StringComparison.OrdinalIgnoreCase)
               || string.Equals(map.Entity.Name, bare, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TableCollector : TSqlFragmentVisitor
    {
        public List<(string Name, string? Alias)> Tables { get; } = [];

        public override void Visit(NamedTableReference node)
        {
            var schema = node.SchemaObject.SchemaIdentifier?.Value;
            var bare = node.SchemaObject.BaseIdentifier.Value;
            Tables.Add((schema is null ? bare : $"{schema}.{bare}", node.Alias?.Value));
        }

        public string? TableFor(string alias)
            => Tables.FirstOrDefault(t => string.Equals(t.Alias, alias, StringComparison.OrdinalIgnoreCase)).Name;
    }

    private sealed class ColumnCollector : TSqlFragmentVisitor
    {
        public List<(string? Qualifier, string Column)> Columns { get; } = [];

        public override void Visit(ColumnReferenceExpression node)
        {
            var parts = node.MultiPartIdentifier?.Identifiers;
            if (parts is null || parts.Count == 0)
            {
                return;
            }

            Columns.Add(parts.Count == 1 ? (null, parts[0].Value) : (parts[^2].Value, parts[^1].Value));
        }
    }
}
