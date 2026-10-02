using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// An owning reference of the row under <paramref name="Alias"/> compared with the row under
/// <paramref name="Target"/>, standing for <paramref name="Conjuncts"/> - the equalities of
/// its foreign key columns with the key they reference (<see cref="ColumnMember.ReferenceComparisons"/>).
/// </summary>
public sealed record ReferenceComparison(string Alias, string Navigation, string Target, IReadOnlyList<ConditionNode> Conjuncts)
{
    /// <summary>Whether the conjunct is one of those this comparison stands for - the same node, not an equal one.</summary>
    public bool StandsFor(ConditionNode conjunct) => Conjuncts.Any(c => ReferenceEquals(c, conjunct));
}

/// <summary>
/// The member a column of an entity is reached through in a query language over entities -
/// HQL, JPQL, LINQ -, which names properties rather than columns. A column a scalar property
/// maps is that property. A foreign key column no scalar property maps - the usual shape in
/// Jakarta Persistence, where the column belongs to the reference alone - is the key of the
/// referenced entity reached through the owning reference that holds the column:
/// <c>o.customer.id</c> for <c>Orders.customer_CustomerID</c>. Writing the column's name in
/// its place named a property no mapping declares, which every one of the four targets
/// refuses. Kept here so that the three visitors and the builders answer it alike (S1).
/// </summary>
public static class ColumnMember
{
    /// <summary>
    /// The member path the column is written as, relative to the alias of its entity; null
    /// where neither a scalar property nor an owning reference maps it, which leaves the
    /// fallback to the caller.
    /// </summary>
    public static string? PathOf(EntityMap? map, string column)
    {
        if (map is null)
        {
            return null;
        }

        if (ScalarOf(map, column) is { } scalar)
        {
            return scalar.Property.Name;
        }

        return HeldBy(map, column) is { } held
            ? $"{held.Reference.SourceNavigationProperty}.{held.Pair.Target.Property.Name}"
            : null;
    }

    /// <summary>
    /// The property map that types the column: the scalar property mapping it, or the key
    /// part a foreign key column held by an owning reference points at - the column holds a
    /// value of that key. Null where nothing maps the column.
    /// </summary>
    public static PropertyMap? TypedBy(EntityMap? map, string column)
        => map is null ? null : ScalarOf(map, column) ?? HeldBy(map, column)?.Pair.Target;

    /// <summary>The scalar property of the entity that maps the column, by its column name or, where none is stated, by its own name.</summary>
    public static PropertyMap? ScalarOf(EntityMap map, string column)
        => map.PropertyMaps.FirstOrDefault(p =>
            string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The conjuncts of a join condition that together say an owning reference of one row
    /// points at the other row: every pair of the reference, its foreign key column - one no
    /// scalar property maps - against the key column it references, under the alias of the
    /// referenced entity. A language over entities says that as the reference compared with
    /// the row, <c>o.customer = c</c>, which every target reads off the foreign key columns
    /// without a join, a composite key included (measured against NHibernate 5.7.0,
    /// Hibernate 7.4.5 and EclipseLink 5.0.0, inner and left joins, both directions) -
    /// whereas reaching a part of the key through the reference costs a join in some of
    /// them. The usual source of such a condition is a join along an association path,
    /// which the parsers derive from the relation (decision 101).
    /// </summary>
    public static IReadOnlyList<ReferenceComparison> ReferenceComparisons(
        ConditionNode condition, IReadOnlyDictionary<string, EntityMap> entities)
    {
        var matched = new List<(string Alias, Relation Reference, string Target, ColumnPair Pair, ConditionNode Conjunct)>();

        foreach (var conjunct in Conjuncts(condition))
        {
            if (conjunct is not ComparisonCondition { Operator: ComparisonOperator.Equal, Right: { } right } comparison)
            {
                continue;
            }

            var match = Match(comparison.Left, right, entities) ?? Match(right, comparison.Left, entities);
            if (match is { } found)
            {
                matched.Add((found.Alias, found.Reference, found.Target, found.Pair, conjunct));
            }
        }

        var comparisons = new List<ReferenceComparison>();

        foreach (var group in matched.GroupBy(m => (m.Alias, m.Reference, m.Target)))
        {
            var pairs = group.Select(m => m.Pair).Distinct().ToList();
            var reference = group.Key.Reference;

            // Only the whole key is the reference: a part of it compared alone says less.
            if (pairs.Count == reference.ColumnPairs.Count && group.Count() == pairs.Count && reference.ColumnPairs.All(pairs.Contains))
            {
                comparisons.Add(new ReferenceComparison(
                    group.Key.Alias, reference.SourceNavigationProperty!, group.Key.Target, [.. group.Select(m => m.Conjunct)]));
            }
        }

        return comparisons;
    }

    /// <summary>The conjuncts of a condition: the operands of its conjunctions, nested ones flattened, or the condition itself.</summary>
    public static IEnumerable<ConditionNode> Conjuncts(ConditionNode condition)
        => condition is LogicalCondition { Operator: LogicalOperator.And } and
            ? and.Operands.SelectMany(Conjuncts)
            : [condition];

    /// <summary>
    /// The foreign key side and the referenced side of one equality, where the first operand
    /// is a foreign key column of an owning reference no scalar property maps and the second
    /// the key column it references, under another alias whose entity is the referenced one.
    /// </summary>
    private static (string Alias, Relation Reference, string Target, ColumnPair Pair)? Match(
        QueryOperand foreignKey, QueryOperand key, IReadOnlyDictionary<string, EntityMap> entities)
    {
        if (!IsPlainColumn(foreignKey) || !IsPlainColumn(key)
            || string.Equals(foreignKey.Table, key.Table, StringComparison.OrdinalIgnoreCase)
            || !entities.TryGetValue(foreignKey.Table!, out var holder)
            || !entities.TryGetValue(key.Table!, out var referenced)
            || ScalarOf(holder, foreignKey.Property!) is not null
            || HeldBy(holder, foreignKey.Property!) is not { } held
            || !string.Equals(referenced.Entity.Name, SimpleEntityName(held.Reference.TargetEntity), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(held.Pair.Target.ColumnName ?? held.Pair.Target.Property.Name, key.Property, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return (foreignKey.Table!, held.Reference, key.Table!, held.Pair);
    }

    private static bool IsPlainColumn(QueryOperand operand)
        => operand is { IsColumn: true, IsAggregate: false, Table: not null } && operand.Property != "*";

    /// <summary>The class a relation's target names, trimmed of an assembly or a namespace (hbm.xml keeps <c>class="Shop.Customer, Shop"</c>).</summary>
    private static string SimpleEntityName(string name)
    {
        var typeName = name.Split(',')[0].Trim();
        var lastDot = typeName.LastIndexOf('.');

        return lastDot < 0 ? typeName : typeName[(lastDot + 1)..];
    }

    /// <summary>
    /// The owning reference that holds the column as a part of its foreign key, with the
    /// pair that names it - asked only of a column no scalar property maps. Only an owning
    /// reference holds its key on this entity (decision 012); an N:M relation keeps its key
    /// on the junction entity (decision 005), and a relation without a navigation property
    /// has no member to reach the key through.
    /// </summary>
    public static (Relation Reference, ColumnPair Pair)? HeldBy(EntityMap map, string column)
    {
        foreach (var relation in map.Relations)
        {
            if (relation.Role != RelationRole.Owning
                || relation.Cardinality == Cardinality.ManyToMany
                || relation.SourceNavigationProperty is null)
            {
                continue;
            }

            var pair = relation.ColumnPairs.FirstOrDefault(p =>
                string.Equals(p.Source.ColumnName ?? p.Source.Property.Name, column, StringComparison.OrdinalIgnoreCase));

            if (pair is not null)
            {
                return (relation, pair);
            }
        }

        return null;
    }
}
