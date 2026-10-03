using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Common.Naming;
using Microsoft.CodeAnalysis.CSharp;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Emits a LINQ method chain, which is EF Core's own query form (decision 022).
///
/// The artifact is a method taking a <c>DbContext</c> and returning <c>IQueryable</c>, with
/// the root written as <c>ctx.Set&lt;T&gt;()</c>. Both are deliberate and both are what make
/// the third verification level possible: a query that returns a list would have to be
/// executed to be judged, and a root naming a DbSet property would need a generated context
/// class the query builder has no way to produce (decision 027).
/// </summary>
public class EFCoreLinqQueryBuilder : AbstractQueryBuilder
{
    private LinqScope scope = new();
    private EFCoreLinqQueryVisitor visitor = null!;
    private readonly List<string> tupleAliases = [];

    /// <summary>
    /// The alias of every join of the chain being written, the ones still to come included:
    /// the row of a chain past its first join is a lambda parameter that sits beside the
    /// joined alias in the result selector of each later join, so it must not take a name a
    /// later join declares - (t, t) => ... does not compile.
    /// </summary>
    private List<string> joinAliases = [];
    private string orderingAfterProjection = string.Empty;

    /// <summary>
    /// The visitor of the scope a subquery is being rendered inside, so that the nested
    /// visitor can resolve correlated references to the outer lambda's parameter; null at
    /// the top level (decision 061).
    /// </summary>
    private EFCoreLinqQueryVisitor? enclosingVisitor;

    /// <summary>
    /// Lambda parameters of every enclosing scope. C# forbids a nested lambda parameter
    /// shadowing an enclosing one, so the nested source step renames on collision.
    /// </summary>
    private HashSet<string> enclosingParams = new(StringComparer.Ordinal);

    /// <summary>
    /// Set while composing a subquery operand: the operator decides the chain's ending -
    /// a bare one-column Select for IN, no projection for EXISTS, a terminal aggregate call
    /// appended by the renderer for a scalar comparison (decision 061).
    /// </summary>
    private ComparisonOperator? operandContext;

    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    /// <summary>What LINQ does not speak goes out as native SQL through SqlQuery or FromSql (decision 113).</summary>
    protected override AbstractQueryBuilder NativeSqlBuilder() => new EFCoreNativeSqlQueryBuilder();

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        // An intermediate result is the local variable that holds its chain (decision 112);
        // its element is an anonymous type, so it names no entity.
        var definition = IsDefinition(clauses.From.Table);
        var map = EntityFor(clauses.From.Table);
        var entity = definition ? null : map?.Entity.Name ?? SingularOf(clauses.From.Table);

        scope = new LinqScope
        {
            Entities = AliasedEntities(clauses),
            Param = FreshName(clauses.From.Alias ?? "c"),
        };
        scope.ElementParam = FreshName(scope.Param == "e" ? "el" : "e");
        scope.Aliases.Add(clauses.From.Alias ?? clauses.From.Table);

        visitor = new EFCoreLinqQueryVisitor(
            scope,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery,
            Expressions,
            enclosingVisitor);

        tupleAliases.Clear();
        tupleAliases.Add(clauses.From.Alias ?? entity ?? clauses.From.Table);
        orderingAfterProjection = string.Empty;

        artifact.ResultEntity = entity;
        artifact.Source.Append(definition ? Variables[clauses.From.Table] : $"ctx.Set<{entity}>()");

        if (map is null && !definition)
        {
            Report(
                ConversionRecordKind.Convention,
                $"No entity was mapped to table '{clauses.From.Table}', so the type name '{entity}' was derived from it.",
                QueryFeature.Projection,
                entity: entity);
        }
    }

    protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact)
    {
        joinAliases = [.. clauses.Joins.Select(join => join.RightTableAlias ?? Bare(join.RightTable).ToLowerInvariant())];

        foreach (var join in clauses.Joins)
        {
            var rightAlias = join.RightTableAlias ?? Bare(join.RightTable).ToLowerInvariant();
            var rightMap = EntityFor(join.RightTable);
            var rightEntity = rightMap?.Entity.Name ?? SingularOf(join.RightTable);

            // The inner sequence: the entity's set, or the variable of an intermediate result
            // (decision 112), which EF Core joins as a derived table.
            var rightSequence = IsDefinition(join.RightTable) ? Variables[join.RightTable] : $"ctx.Set<{rightEntity}>()";

            var condition = SplitJoinCondition(join, rightAlias);
            if (condition.Unplaced is { } unplaced)
            {
                ReportUnspoken(
                    $"The condition of the join onto '{join.RightTable}' names the column '{unplaced}' without its table, so a LINQ join cannot tell which of the two rows it belongs to",
                    QueryFeature.Join);
                return;
            }

            // Beyond its keys, a LINQ join can filter only the sequence it joins, which narrows
            // the rows of an inner or a left join exactly and removes rows a right or a full
            // join keeps; a condition naming both rows has no place in it at all. Neither join
            // has a correlated form either, so the query goes out in native SQL (decision 113).
            var beyondKeys = condition.RightOnly.Count > 0 || condition.Rest.Count > 0;
            if (beyondKeys && join.Kind is JoinKind.Right or JoinKind.Full)
            {
                ReportUnspoken(
                    $"The {(join.Kind == JoinKind.Right ? "right" : "full")} join onto '{join.RightTable}' has a condition beyond equalities of columns of the two rows, which LINQ can put neither into the keys of the join nor into a filter of either sequence",
                    QueryFeature.Join);
                return;
            }

            var leftParam = scope.Param;

            // The lambda over the joined sequence's rows: the alias, unless a lambda around this
            // one or a local of the method already holds the name, or - nested in a SelectMany -
            // the row of the chain does.
            var innerParam = rightAlias;
            for (var i = 1; innerParam == leftParam || enclosingParams.Contains(innerParam) || Variables.ContainsValue(innerParam); i++)
            {
                innerParam = rightAlias + i;
            }

            var members = scope.Composite
                ? string.Join(", ", tupleAliases.Select(a => $"{leftParam}.{a}")) + $", {rightAlias}"
                : $"{leftParam}, {rightAlias}";
            var resultSelector = $"({leftParam}, {rightAlias}) => new {{ {members} }}";

            // A condition that names both rows other than as keys, or no key at all, is the
            // correlated SelectMany (decision 113): the joined sequence filtered by the whole
            // condition for each row of the chain, which EF Core translates to a join with that
            // condition - and to a left one over DefaultIfEmpty(), which keeps a row of the
            // chain that found no match.
            if (condition.Rest.Count > 0 || condition.Pairs.Count == 0)
            {
                if (InnerPredicate(join, rightAlias, innerParam, join.OnCondition, correlatedTo: leftParam) is not { } correlated)
                {
                    return;
                }

                var collection = $"{rightSequence}.Where({innerParam} => {correlated})"
                                 + (join.Kind == JoinKind.Left ? ".DefaultIfEmpty()" : string.Empty);

                artifact.Joins.Append($"\n        .SelectMany({leftParam} => {collection}, {resultSelector})");
                AdvanceScope(join, rightAlias);
                continue;
            }

            // Conjuncts that name the joined row alone filter the joined sequence before the
            // join (decision 113): exact for an inner join, and for a left one too, where a row
            // of the chain that finds no match keeps its row exactly as under the ON.
            if (condition.RightOnly.Count > 0)
            {
                var filter = condition.RightOnly.Count == 1
                    ? condition.RightOnly[0]
                    : new LogicalCondition(LogicalOperator.And, condition.RightOnly);

                if (InnerPredicate(join, rightAlias, innerParam, filter, correlatedTo: null) is not { } predicate)
                {
                    return;
                }

                rightSequence += $".Where({innerParam} => {predicate})";
            }

            // The joined row's entity as the scope resolves its alias - through the naming
            // convention too, where the source states no table (decision 050) -, so that the
            // key selectors type its members as the class declares them.
            var keyMap = scope.Entities.GetValueOrDefault(join.RightTableAlias ?? join.RightTable) ?? rightMap;
            var (leftKeys, rightKeys) = KeySelectors(condition.Pairs, keyMap, innerParam);

            var arguments =
                $"{rightSequence}, " +
                $"{leftParam} => {leftKeys}, " +
                $"{innerParam} => {rightKeys}, " +
                resultSelector;

            if (join.Kind == JoinKind.Full)
            {
                // EF Core 10 has LeftJoin and RightJoin but no full outer join, and an inner
                // join in its place would return different rows (decision 065). The full join
                // is composed from the operators that do exist: the left join's rows,
                // concatenated with the right join's rows that found no left match. Concat is
                // UNION ALL, so matched pairs are not doubled - the filter excludes them from
                // the right branch - and genuine duplicates are not collapsed the way Union
                // would. The filter stands on the root member because that one is never null
                // in the left branch. A faithful translation is neither a loss nor a
                // convention, so no record is issued.
                var root = scope.Composite ? tupleAliases[0] : leftParam;
                var probe = FreshName("x");
                var chainSoFar = string.Concat(artifact.Source.ToString(), artifact.Joins.ToString());

                artifact.Joins.Append(
                    $"\n        .LeftJoin({arguments})" +
                    $"\n        .Concat({chainSoFar}" +
                    $"\n            .RightJoin({arguments})" +
                    $"\n            .Where({probe} => {probe}.{root} == null))");
            }
            else
            {
                var method = join.Kind switch
                {
                    JoinKind.Inner => "Join",
                    JoinKind.Left => "LeftJoin",
                    JoinKind.Right => "RightJoin",
                    _ => null,
                };

                if (method is null)
                {
                    // No catch-all translation: a JoinKind value without an operator must not
                    // come out as a neighbouring join (decision 053).
                    Report(
                        ConversionRecordKind.Failure,
                        $"Join kind {join.Kind} has no LINQ operator; no artifact was generated.",
                        QueryFeature.JoinKind);
                    continue;
                }

                artifact.Joins.Append($"\n        .{method}({arguments})");
            }

            AdvanceScope(join, rightAlias);
        }
    }

    /// <summary>From here on the lambdas of the chain range over the joined row, which carries the joined table under its alias.</summary>
    private void AdvanceScope(JoinInstruction join, string rightAlias)
    {
        var declared = join.RightTableAlias ?? join.RightTable;

        // An outer join leaves the rows of one side or both missing where it finds no match,
        // and a COUNT over a column of such a row counts the match, not the row (decision 053).
        if (join.Kind is JoinKind.Right or JoinKind.Full)
        {
            scope.OptionalAliases.UnionWith(scope.Aliases);
        }

        if (join.Kind is JoinKind.Left or JoinKind.Full)
        {
            scope.OptionalAliases.Add(declared);
        }

        tupleAliases.Add(rightAlias);
        scope.Aliases.Add(declared);
        scope.Composite = true;
        scope.Param = FreshParam();
    }

    /// <summary>
    /// The two key selectors of a LINQ join over the column pairs of its condition. A single
    /// key is the member itself: C# infers the key type even where one side is nullable and
    /// the other not. A composite key is an anonymous type on each side, and the two are one
    /// type only where their members agree in name and in type, so a member whose nullability
    /// differs from its counterpart's is cast to the nullable form - a foreign key the entity
    /// declares nullable beside the key it points at -, and where the members would be named
    /// differently, or a cast leaves one without an inferred name, both sides name them after
    /// the joined side's members.
    /// </summary>
    private (string Left, string Right) KeySelectors(
        IReadOnlyList<(QueryOperand Left, QueryOperand Right)> pairs, EntityMap? rightMap, string innerParam)
    {
        var left = pairs.Select(p => visitor.Operand(p.Left)).ToList();
        var right = pairs
            .Select(p => $"{innerParam}.{EFCoreColumnMember.PathOf(rightMap, p.Right.Property!) ?? p.Right.Property}")
            .ToList();

        if (pairs.Count == 1)
        {
            return (left[0], right[0]);
        }

        var names = right.Select(InferredMember).ToList();
        var named = left.Select(InferredMember).Where((name, i) => name != names[i]).Any()
                    || names.Distinct(StringComparer.Ordinal).Count() != names.Count;

        for (var i = 0; i < pairs.Count; i++)
        {
            var leftMap = pairs[i].Left.Table is { } table ? scope.Entities.GetValueOrDefault(table) : null;
            var leftValue = EFCoreColumnMember.DeclaredValue(leftMap, pairs[i].Left.Property!);
            var rightValue = EFCoreColumnMember.DeclaredValue(rightMap, pairs[i].Right.Property!);

            if (leftValue is not { } l || rightValue is not { } r || l.Scalar != r.Scalar || l.Nullable == r.Nullable)
            {
                continue;
            }

            var nullable = $"({CSharpTypeConvertor.ToString(LangType.Scalar(l.Scalar))}?)";
            if (l.Nullable)
            {
                right[i] = nullable + right[i];
            }
            else
            {
                left[i] = nullable + left[i];
            }

            named = true;
        }

        if (named)
        {
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            {
                names = [.. names.Select((_, i) => $"Key{i + 1}")];
            }

            left = [.. left.Select((key, i) => $"{names[i]} = {key}")];
            right = [.. right.Select((key, i) => $"{names[i]} = {key}")];
        }

        return ($"new {{ {string.Join(", ", left)} }}", $"new {{ {string.Join(", ", right)} }}");
    }

    /// <summary>
    /// A part of a join condition written as the body of a lambda over the joined row
    /// (decision 113): the joined table is that lambda's parameter, every other alias is
    /// reached through the scope of the chain the way a correlated subquery reaches the
    /// query around it (decision 061). A subquery inside it correlates through the same
    /// scope, which is why the builder's own scope is this one while it is written. Null
    /// when a part could not be written, the reason being on the channel already.
    /// </summary>
    /// <param name="correlatedTo">The parameter of the lambda the predicate is nested in - the row of the chain in a SelectMany -, which no lambda inside it may shadow; null where the predicate stands outside any.</param>
    private string? InnerPredicate(JoinInstruction join, string rightAlias, string innerParam, ConditionNode predicate, string? correlatedTo)
    {
        var declared = join.RightTableAlias ?? join.RightTable;
        var inner = new LinqScope
        {
            Entities = new Dictionary<string, EntityMap>(scope.Entities, StringComparer.OrdinalIgnoreCase),
            Param = innerParam,
        };
        inner.ElementParam = FreshName(innerParam == "e" ? "el" : "e");
        inner.Aliases.Add(declared);
        inner.Aliases.Add(rightAlias);
        if (inner.Entities.TryGetValue(declared, out var map))
        {
            inner.Entities.TryAdd(rightAlias, map);
        }

        var innerVisitor = new EFCoreLinqQueryVisitor(
            inner,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery,
            Expressions,
            visitor);

        var savedScope = scope;
        var savedVisitor = visitor;
        var savedEnclosingParams = enclosingParams;

        scope = inner;
        visitor = innerVisitor;
        if (correlatedTo is not null)
        {
            enclosingParams = new HashSet<string>(enclosingParams, StringComparer.Ordinal) { correlatedTo };
        }

        try
        {
            var text = predicate.Accept(innerVisitor);
            return text.Length == 0 ? null : text;
        }
        finally
        {
            scope = savedScope;
            visitor = savedVisitor;
            enclosingParams = savedEnclosingParams;
        }
    }

    private string FreshParam()
    {
        foreach (var candidate in new[] { "t", "row", "q", "z" })
        {
            if (!tupleAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                && !joinAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                && !enclosingParams.Contains(candidate)
                && !Variables.ContainsValue(candidate))
            {
                return candidate;
            }
        }

        return "row" + tupleAliases.Count;
    }

    /// <summary>
    /// A name not taken by any enclosing lambda parameter (decision 061), nor by a variable
    /// that holds an intermediate result (decision 112): C# forbids a lambda parameter that
    /// shadows a local of the method, and the alias of a derived table is its name.
    /// </summary>
    private string FreshName(string candidate)
    {
        var name = candidate;
        for (int i = 1; enclosingParams.Contains(name) || Variables.ContainsValue(name); i++)
        {
            name = candidate + i;
        }

        return name;
    }

    private IReadOnlyList<WithInstruction>? variablesOf;
    private Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The local variable of the generated method that holds each intermediate result
    /// (decision 112): the definition's own name, made a C# identifier where it is a
    /// keyword, and numbered on where it would clash with the context or a parameter of the
    /// method - the variable is a detail of the generated code, as a lambda parameter is,
    /// and is renamed on collision the way a lambda parameter is. Derived once per Build,
    /// before the first step names a lambda parameter.
    /// </summary>
    private Dictionary<string, string> Variables
    {
        get
        {
            if (ReferenceEquals(variablesOf, Definitions))
            {
                return variables;
            }

            variablesOf = Definitions;
            variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var taken = new HashSet<string>(StringComparer.Ordinal) { "ctx" };
            foreach (var parameter in Parameters)
            {
                taken.Add(QueryParameterNaming.IdentifierFor(parameter));
            }

            foreach (var definition in Definitions)
            {
                var name = SyntaxFacts.GetKeywordKind(definition.Name) == SyntaxKind.None ? definition.Name : "@" + definition.Name;
                var candidate = name;
                for (var i = 1; taken.Contains(candidate); i++)
                {
                    candidate = name + i;
                }

                taken.Add(candidate);
                variables[definition.Name] = candidate;
            }

            return variables;
        }
    }

    /// <summary>
    /// The declarations of the intermediate results, one statement each, in the order the
    /// query defines them, so that a definition reading an earlier one finds its variable
    /// declared (decision 112). Each body is a chain composed through the eight steps, from
    /// its own root, exactly as an operand of a set operation is. Null when a body could not
    /// be rendered and the reason is on the channel.
    /// </summary>
    private string? DefinitionStatements()
    {
        var statements = new System.Text.StringBuilder();

        foreach (var definition in Definitions)
        {
            var clauses = NormalizeDefinition(definition, out var setOperation);
            var chain = setOperation is not null
                ? RenderSetOperation(setOperation, out _)
                : clauses is not null ? ComposeChain(clauses) : null;

            if (chain is null)
            {
                return null;
            }

            statements.Append($"var {Variables[definition.Name]} = {chain};\n    ");
        }

        return statements.ToString();
    }

    /// <summary>One set of clauses as a whole chain from its root, with the scope it leaves behind restored.</summary>
    private string ComposeChain(QueryClauses clauses)
    {
        var savedScope = scope;
        var savedVisitor = visitor;
        var savedTuples = tupleAliases.ToList();
        var savedJoinAliases = joinAliases;
        var savedOrderingAfter = orderingAfterProjection;

        var artifact = Compose(clauses);
        var chain = string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);

        scope = savedScope;
        visitor = savedVisitor;
        tupleAliases.Clear();
        tupleAliases.AddRange(savedTuples);
        joinAliases = savedJoinAliases;
        orderingAfterProjection = savedOrderingAfter;

        return chain;
    }

    /// <summary>
    /// The conjuncts of a join condition sorted by where LINQ can put them (decision 113).
    /// <paramref name="Pairs"/> are equalities of a column of the chain's row and a column of
    /// the joined row, which go into the two key selectors; <paramref name="RightOnly"/> names
    /// no row of the chain - the joined row, values, a row of a query around this one -, and
    /// filters the joined sequence; <paramref name="Rest"/> names a row of the chain otherwise,
    /// or holds a subquery, which may. <paramref name="Unplaced"/> is a column the condition
    /// names without a table, which belongs to neither side for certain.
    /// </summary>
    private sealed record JoinCondition(
        List<(QueryOperand Left, QueryOperand Right)> Pairs,
        List<ConditionNode> RightOnly,
        List<ConditionNode> Rest,
        QueryOperand? Unplaced);

    /// <summary>
    /// Sorts the conjuncts of the join's condition (<see cref="JoinCondition"/>). The rows of
    /// the chain are the aliases the scope has declared so far - the source and the joins
    /// before this one -, the joined row is this join's alias, or its table where it has none.
    /// </summary>
    private JoinCondition SplitJoinCondition(JoinInstruction join, string rightAlias)
    {
        var conjuncts = new List<ConditionNode>();
        Conjuncts(join.OnCondition, conjuncts);

        bool IsRight(string? table)
            => table is not null
               && (string.Equals(table, join.RightTableAlias ?? join.RightTable, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(table, rightAlias, StringComparison.OrdinalIgnoreCase));

        bool IsLeft(string? table) => table is not null && !IsRight(table) && scope.Aliases.Contains(table);

        var split = new JoinCondition([], [], [], null);

        foreach (var conjunct in conjuncts)
        {
            if (conjunct is ComparisonCondition { Operator: ComparisonOperator.Equal, Left: { IsColumn: true, IsAggregate: false } left, Right: { IsColumn: true, IsAggregate: false } right })
            {
                if (IsRight(right.Table) && IsLeft(left.Table))
                {
                    split.Pairs.Add((left, right));
                    continue;
                }

                if (IsRight(left.Table) && IsLeft(right.Table))
                {
                    split.Pairs.Add((right, left));
                    continue;
                }
            }

            var leaves = LeavesOf(conjunct).ToList();
            if (leaves.FirstOrDefault(leaf => leaf.IsColumn && leaf.Table is null) is { } unqualified)
            {
                return split with { Unplaced = unqualified };
            }

            var namesTheChain = leaves.Any(leaf => leaf.IsSubQuery || (leaf.IsColumn && IsLeft(leaf.Table)));
            (namesTheChain ? split.Rest : split.RightOnly).Add(conjunct);
        }

        return split;
    }

    /// <summary>The operands of a top-level conjunction, nested ones flattened; any other condition is a single conjunct.</summary>
    private static void Conjuncts(ConditionNode node, List<ConditionNode> into)
    {
        if (node is LogicalCondition { Operator: LogicalOperator.And } conjunction)
        {
            foreach (var operand in conjunction.Operands)
            {
                Conjuncts(operand, into);
            }

            return;
        }

        into.Add(node);
    }

    /// <summary>The leaves of a condition short of a subquery: columns, values, parameters, and the subqueries themselves.</summary>
    private static IEnumerable<QueryOperand> LeavesOf(ConditionNode node) => node switch
    {
        ComparisonCondition comparison => comparison.Right is null
            ? LeavesOf(comparison.Left)
            : LeavesOf(comparison.Left).Concat(LeavesOf(comparison.Right)),
        LogicalCondition logical => logical.Operands.SelectMany(LeavesOf),
        NotCondition negation => LeavesOf(negation.Operand),
        _ => [],
    };

    private static IEnumerable<QueryOperand> LeavesOf(QueryOperand operand)
    {
        if (operand.IsExpression)
        {
            return OperandStructure.Inside(operand.Expression!).SelectMany(LeavesOf);
        }

        if (operand.IsValueList)
        {
            return operand.Values!.SelectMany(LeavesOf);
        }

        return [operand];
    }

    protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Filter is null)
        {
            return;
        }

        artifact.Filter.Append($"\n        .Where({scope.Param} => {clauses.Filter.Accept(visitor)})");
    }

    protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.GroupBys.Count == 0)
        {
            return;
        }

        // One key is g.Key itself. Several go into an anonymous key object, each under a name:
        // a column under its property, which C# infers from the member access, an expression
        // under the alias of the projection that projects the same value. An expression no
        // projection names has no name the tool may give it (decision 028), and LINQ cannot
        // say the grouping without one, so the query goes out in native SQL (decision 113).
        var keys = new List<LinqGroupKey>(clauses.GroupBys.Count);
        var members = new List<string>(clauses.GroupBys.Count);
        foreach (var grouping in clauses.GroupBys)
        {
            var key = grouping.Key;
            if (clauses.GroupBys.Count == 1)
            {
                keys.Add(new LinqGroupKey(key, null));
                members.Add(visitor.Visit(grouping));
                continue;
            }

            if (key.IsColumn)
            {
                keys.Add(new LinqGroupKey(key, InferredMember(visitor.Property(key.Table, key.Property!))));
                members.Add(visitor.Visit(grouping));
                continue;
            }

            var name = clauses.Projections.FirstOrDefault(p => p.Alias is not null && OperandStructure.Same(p.Operand, key))?.Alias;
            if (name is null)
            {
                ReportUnspoken(
                    $"The grouping key '{key}' is an expression no projection names, and a LINQ key of several parts names every member of its key object",
                    QueryFeature.ComputedGrouping);
                return;
            }

            keys.Add(new LinqGroupKey(key, name));
            members.Add($"{name} = {visitor.Visit(grouping)}");
        }

        var selector = members.Count == 1 ? members[0] : $"new {{ {string.Join(", ", members)} }}";

        artifact.Grouping.Append($"\n        .GroupBy({scope.Param} => {selector})");

        // From here on the lambda parameter holds a grouping, which is why the projection
        // step has to run after this one.
        scope.ElementParam = scope.Param;
        scope.Grouped = true;
        scope.GroupKeys = keys;
        scope.Param = "g";
    }

    protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.PostFilter is null)
        {
            return;
        }

        artifact.PostFilter.Append($"\n        .Where({scope.Param} => {clauses.PostFilter.Accept(visitor)})");
    }

    protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.OrderBys.Count == 0)
        {
            return;
        }

        // Under DISTINCT every ordering follows Distinct() (decision 073): EF Core drops an
        // ordering that precedes a Distinct() without a row-limiting operator, so the
        // artifact would lose an ordering the source stated. The keys are projected ones -
        // the template's gate guarantees it - so they are named on the shape after the
        // collapse: the row itself for a whole entity, a member of the projection otherwise.
        if (clauses.Distinct)
        {
            orderingAfterProjection = Chain(
                [.. clauses.OrderBys],
                clauses.ProjectsWholeEntity && !scope.Grouped ? scope.Param : "p",
                o => KeyAfterDistinct(clauses, o));
            return;
        }

        var before = new List<OrderByInstruction>();
        var after = new List<OrderByInstruction>();

        foreach (var order in clauses.OrderBys)
        {
            // Ordering by a projection alias can only happen once the projection exists, so
            // it goes after Select. The slotted artifact is what makes that possible without
            // the step order having to change (decision 023).
            var byAlias = order.Operand is { IsColumn: true, Table: null, IsAggregate: false }
                          && clauses.Projections.Any(p =>
                              string.Equals(p.Alias, order.Operand.Property, StringComparison.OrdinalIgnoreCase));

            (byAlias ? after : before).Add(order);
        }

        // An OrderBy after the projection starts a new ordering, and EF Core discards the one
        // before it (verified against EF Core 10: ORDER BY keeps the later keys alone). So
        // where some keys need the projection and others stand before it, every key goes
        // after it, named by the projected member that carries it - the order of the keys
        // is the source's, and a key left in front would be lost. A key the projection does
        // not carry cannot follow it: with a slice the lost key would select other rows, so
        // the query is refused (decision 053); without one only the order of ties changes.
        if (after.Count > 0 && before.Count > 0)
        {
            if (before.All(o => clauses.Projections.Any(p => Projects(p, o))))
            {
                orderingAfterProjection = Chain([.. clauses.OrderBys], "p", o => KeyAfterDistinct(clauses, o));
                return;
            }

            var lost = before.First(o => !clauses.Projections.Any(p => Projects(p, o)));
            if (clauses.Offset is not null || clauses.Limit is not null)
            {
                ReportUnspoken(
                    $"The ordering key '{lost.Operand}' is not projected and stands before a key that names the projection; a LINQ ordering after the projection discards the one before it, so the slice would select other rows",
                    QueryFeature.Ordering);
                return;
            }

            Report(
                ConversionRecordKind.Loss,
                $"The ordering key '{lost.Operand}' is not projected and stands before a key that names the projection; a LINQ ordering after the projection discards the one before it, so the rows come back ordered by the keys after the projection alone.",
                QueryFeature.Ordering);
        }

        artifact.Ordering.Append(Chain(before, scope.Param, o => visitor.Visit(o)));
        orderingAfterProjection = Chain(after, "p", o => $"p.{o.Operand.Property}");
    }

    /// <summary>
    /// The ordering key as the element after Distinct() spells it (decision 073): the row
    /// itself for a whole entity, a member of the grouping key for a grouped whole-entity
    /// projection, and a member of the projected anonymous type otherwise.
    /// </summary>
    private string KeyAfterDistinct(QueryClauses clauses, OrderByInstruction order)
    {
        if (clauses.ProjectsWholeEntity)
        {
            var key = visitor.Visit(order);
            var groupKey = $"{scope.Param}.Key";
            return scope.Grouped && key.StartsWith(groupKey, StringComparison.Ordinal)
                ? "p" + key[groupKey.Length..]
                : key;
        }

        var projection = clauses.Projections.FirstOrDefault(p => Projects(p, order));
        var member = projection?.Alias ?? MemberName(projection?.Operand ?? order.Operand);
        return $"p.{member}";
    }

    /// <summary>
    /// The member a projected operand is named by in the anonymous type when it carries no
    /// alias: the property behind a column - an aggregate over a column included, as it has
    /// been all along. An expression without an alias never reaches here: the template's
    /// gate refuses it (decision 107).
    /// </summary>
    private string MemberName(QueryOperand operand)
        => operand.IsColumn ? InferredMember(visitor.Property(operand.Table, operand.Property!)) : operand.ToString();

    /// <summary>
    /// The member C# infers for an anonymous type from a member access: the last name of the
    /// path - <c>id</c> for a foreign key column written through its navigation as
    /// <c>customer.id</c> (<see cref="ColumnMember"/>), the property itself otherwise.
    /// </summary>
    private static string InferredMember(string path) => path[(path.LastIndexOf('.') + 1)..];

    private static string Chain(
        List<OrderByInstruction> orders,
        string param,
        Func<OrderByInstruction, string> key)
    {
        var text = string.Empty;
        for (int i = 0; i < orders.Count; i++)
        {
            var method = (i == 0, orders[i].Asc) switch
            {
                (true, true) => "OrderBy",
                (true, false) => "OrderByDescending",
                (false, true) => "ThenBy",
                (false, false) => "ThenByDescending",
            };

            text += $"\n        .{method}({param} => {key(orders[i])})";
        }

        return text;
    }

    protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact)
    {
        // Inside a subquery operand the operator owns the ending (decision 061): EXISTS
        // needs no projection under its Any(), IN needs the single column bare - an
        // anonymous type would not Contains against the outer operand - and a scalar
        // comparison gets its terminal aggregate appended by the renderer.
        if (operandContext is { } context)
        {
            if (context == ComparisonOperator.In)
            {
                artifact.Projection.Append(
                    $"\n        .Select({scope.Param} => {visitor.Visit(clauses.Projections[0])})");
            }

            // IN and EXISTS carry a DISTINCT literally (decision 073). A scalar operand never
            // arrives with one: it is a single ungrouped aggregate, over which the template's
            // gate has already left the collapse out as the identity it is.
            AppendDistinct(clauses, artifact);
            return;
        }

        // Rule Q3: no projection means the whole entity is materialized, and in LINQ that is
        // simply the absence of a Select.
        if (clauses.ProjectsWholeEntity && !scope.Grouped)
        {
            AppendDistinct(clauses, artifact);
            return;
        }

        if (clauses.ProjectsWholeEntity)
        {
            artifact.Projection.Append($"\n        .Select({scope.Param} => {scope.Param}.Key)");
            AppendDistinct(clauses, artifact);
            return;
        }

        var members = clauses.Projections
            .Select(p => $"{p.Alias ?? MemberName(p.Operand)} = {visitor.Visit(p)}")
            .ToList();

        artifact.Projection.Append($"\n        .Select({scope.Param} => new {{ {string.Join(", ", members)} }})");
        AppendDistinct(clauses, artifact);
    }

    /// <summary>
    /// Distinct() follows the projection it collapses (decision 073), which is why it is
    /// written by the projection step rather than by a step of its own; the ordering and the
    /// slice are appended after it.
    /// </summary>
    private static void AppendDistinct(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Distinct)
        {
            artifact.Projection.Append("\n        .Distinct()");
        }
    }

    protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Offset is null && clauses.Limit is null)
        {
            return;
        }

        // T-SQL counts rows in bigint, Skip and Take in Int32; a value between the two has
        // no faithful LINQ form and dropping it would change which rows come back, so the
        // query goes out in native SQL, which counts in bigint (decision 113). Only a stated
        // number can be out of range: a bound count is typed Int by the template
        // (decision 085) and has nothing left to overflow.
        if (clauses.Offset?.Value > int.MaxValue || clauses.Limit?.Value > int.MaxValue)
        {
            ReportUnspoken(
                "The pagination value exceeds Int32, which Skip and Take cannot carry",
                QueryFeature.Pagination);
            return;
        }

        // A bound count is the parameter of the generated method, captured by the chain the
        // way a parameter of a condition is captured by its lambda.
        if (clauses.Offset is { } offset)
        {
            artifact.Pagination.Append($"\n        .Skip({Spelled(offset)})");
        }

        if (clauses.Limit is { } limit)
        {
            artifact.Pagination.Append($"\n        .Take({Spelled(limit)})");
        }
    }

    /// <summary>
    /// Renders a subquery operand as a nested chain from its own <c>ctx.Set&lt;T&gt;()</c>
    /// root (decision 061). The visitor decides what surrounds it - Contains for IN, Any for
    /// EXISTS, nothing for a scalar - and this renderer decides how the chain ends: bare for
    /// EXISTS, a one-column Select for IN, a terminal aggregate call for a scalar. A scalar
    /// subquery that is not a single ungrouped aggregate has no faithful LINQ form -
    /// First() would silently pick one row where SQL refuses several - and the query goes
    /// out in native SQL, which writes the subquery as the source did (decision 113).
    /// </summary>
    private string? RenderSubQuery(SubQueryInstruction subQuery, ComparisonOperator op)
    {
        var clauses = NormalizeSubQueryOperand(subQuery, op);
        if (clauses is null)
        {
            return null;
        }

        var scalar = op is not (ComparisonOperator.Exists or ComparisonOperator.In);

        // A list joined over a subquery (decision 113): EF Core 10 does not translate
        // string.Join over a chain from a query root, it fetches the rows and joins them on
        // the client - verified -, so the query goes out in native SQL.
        if (scalar && clauses.Projections[0].Operand is { IsExpression: true } listed && listed.Expression!.IsListAggregate)
        {
            ReportUnspoken(
                "string.Join over a subquery is not translated by EF Core 10, which joins the list on the client",
                QueryFeature.ListAggregation);
            return null;
        }

        if (scalar && (clauses.GroupBys.Count > 0 || !clauses.Projections[0].Operand.IsAggregate))
        {
            ReportUnspoken(
                "A scalar subquery that is not a single ungrouped aggregate has no LINQ form - First() would silently pick one row where SQL refuses several",
                QueryFeature.Subquery);
            return null;
        }

        if (op == ComparisonOperator.In
            && clauses.GroupBys.Count == 0
            && clauses.Projections[0].Operand.IsAggregate)
        {
            ReportUnspoken(
                "An aggregate projected without a grouping cannot stand inside a LINQ subquery's Select",
                QueryFeature.Subquery);
            return null;
        }

        var savedScope = scope;
        var savedVisitor = visitor;
        var savedTuples = tupleAliases.ToList();
        var savedJoinAliases = joinAliases;
        var savedOrderingAfter = orderingAfterProjection;
        var savedContext = operandContext;
        var savedEnclosingVisitor = enclosingVisitor;
        var savedEnclosingParams = enclosingParams;

        enclosingVisitor = visitor;
        enclosingParams = new HashSet<string>(enclosingParams, StringComparer.Ordinal)
        {
            savedScope.Param,
            savedScope.ElementParam,
        };
        operandContext = op;

        var artifact = Compose(clauses);

        var chain = string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);

        if (scalar)
        {
            chain += TerminalAggregate(clauses.Projections[0]);
        }

        scope = savedScope;
        visitor = savedVisitor;
        tupleAliases.Clear();
        tupleAliases.AddRange(savedTuples);
        joinAliases = savedJoinAliases;
        orderingAfterProjection = savedOrderingAfter;
        operandContext = savedContext;
        enclosingVisitor = savedEnclosingVisitor;
        enclosingParams = savedEnclosingParams;

        // The chain was written for a multi-line method body; embedded in a condition it
        // reads as one expression, so the step breaks are flattened.
        return chain.Replace("\n        ", "");
    }

    /// <summary>
    /// The terminal call a scalar subquery's single aggregate becomes. Over the distinct
    /// values of the column (decision 102) it is a Select of the column, collapsed, then
    /// the parameterless aggregate - <c>.Select(o =&gt; o.CustomerId).Distinct().Count()</c>.
    /// </summary>
    private string TerminalAggregate(ProjectInstruction projection)
    {
        var aggregated = projection.Operand;
        var bare = aggregated.Bare();

        if (aggregated.Function == "COUNT" && !aggregated.Distinct)
        {
            if (bare.Property != "*")
            {
                Report(
                    ConversionRecordKind.Convention,
                    $"COUNT({bare}) was written as Count(), which counts rows rather than non-null values.",
                    QueryFeature.Aggregation);
            }

            return ".Count()";
        }

        var method = aggregated.Function switch
        {
            "COUNT" => "Count",
            "SUM" => "Sum",
            "MIN" => "Min",
            "MAX" => "Max",
            "AVG" => "Average",
            _ => null,
        };

        if (method is null)
        {
            Report(
                ConversionRecordKind.Failure,
                $"Aggregate function {aggregated.Function} has no LINQ counterpart; the query was not generated.",
                QueryFeature.Aggregation);
            return string.Empty;
        }

        // The argument the aggregate ranges over - a column, or an expression (decision 107)
        // - written over the row of the nested scope.
        var argument = $"{scope.Param} => {visitor.Operand(bare)}";
        return aggregated.Distinct
            ? $".Select({argument}).Distinct().{method}()"
            : $".{method}({argument})";
    }

    protected override List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        var chain = RenderSetOperation(instruction, out var elementEntity);
        if (chain is null)
        {
            return [];
        }

        var returnType = elementEntity is not null ? $"IQueryable<{elementEntity}>" : "IQueryable";
        if (DefinitionStatements() is not { } statements)
        {
            return [];
        }

        var method =
            $$"""
            public static {{returnType}} {{MethodName}}(DbContext ctx{{CSharpParameters()}})
            {
                {{statements}}return {{chain}};
            }
            """;

        return [new() { Content = method, ContentType = ConversionContentType.CSharpQuery }];
    }

    private string? RenderSetOperation(SetOperationInstruction instruction, out string? elementEntity)
    {
        elementEntity = null;

        var left = RenderOperandChain(instruction.Left, out var leftEntity);
        var right = RenderOperandChain(instruction.Right, out var rightEntity);
        if (left is null || right is null)
        {
            return null;
        }

        // LINQ set operations compose two queryables of one element type. Two whole-entity
        // operands of the same entity keep that type; two projections each build an anonymous
        // type and are left to the compilation level to judge. What cannot type-check at all
        // is a whole entity against anything else, and emitting it anyway would only move the
        // error into the consumer's build (decision 053) - so the query goes out in native
        // SQL, where a set operation composes rows rather than types (decision 113).
        if (!string.Equals(leftEntity, rightEntity, StringComparison.Ordinal))
        {
            ReportUnspoken(
                "The two sides of the set operation materialize different element types, which LINQ cannot compose",
                QueryFeature.SetOperation);
            return null;
        }

        var method = visitor.Visit(instruction);
        if (method.Length == 0)
        {
            return null;
        }

        elementEntity = leftEntity;
        return $"{left}\n        .{method}({right})";
    }

    /// <summary>
    /// One operand as a full chain from its own <c>ctx.Set&lt;T&gt;()</c> root. The entity
    /// name comes out only when the operand materializes whole entities of a nameable type;
    /// null stands for an anonymous element type.
    /// </summary>
    private string? RenderOperandChain(SubQueryInstruction operand, out string? elementEntity)
    {
        var body = Unwrap(operand.Instructions);

        if (body.Count == 1 && body[0] is SetOperationInstruction nested)
        {
            return RenderSetOperation(nested, out elementEntity);
        }

        elementEntity = null;

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return null;
        }

        var artifact = Compose(clauses);
        elementEntity = clauses.ProjectsWholeEntity && !scope.Composite && !scope.Grouped
            ? artifact.ResultEntity
            : null;

        return string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);
    }

    protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
    {
        var chain = string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);

        // A projection into an anonymous type, a tuple produced by a join and a grouping all
        // have element types the artifact cannot name, so the method is typed non-generically;
        // so has the row of an intermediate result (decision 112).
        var returnType = clauses.ProjectsWholeEntity && !scope.Composite && !scope.Grouped && artifact.ResultEntity is not null
            ? $"IQueryable<{artifact.ResultEntity}>"
            : "IQueryable";

        // The intermediate results as variables before the chain (decision 112), rendered
        // after the chain is assembled, because composing them moves the scope the chain read.
        if (DefinitionStatements() is not { } statements)
        {
            return [];
        }

        // EF Core needs no binding call: the parameters of the method are captured by the
        // lambdas of the chain, which is how the source wrote them too (decision 083).
        var method =
            $$"""
            public static {{returnType}} {{MethodName}}(DbContext ctx{{CSharpParameters()}})
            {
                {{statements}}return {{chain}};
            }
            """;

        return [new() { Content = method, ContentType = ConversionContentType.CSharpQuery }];
    }

    private static string Bare(string table) => EntityTableNaming.BareName(table);

    private static string SingularOf(string table) => EntityTableNaming.EntityNameFor(table);
}
