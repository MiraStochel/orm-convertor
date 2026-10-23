using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace Tests.Verification;

/// <summary>
/// What decision 170 reads off the intermediate representation of a query: the key its
/// ordering falls on among the fields of its result, and whether a slice determines the rows
/// it returns. Both are read from the query, never from data, which is what lets a build hold
/// them without a database.
/// </summary>
internal static class OrderingKeys
{
    /// <summary>
    /// The outermost scope of a read query: the body itself, or the one scope a parser wrapped
    /// it in. A set operation has no ordering of its own in the representation (its ordering
    /// is a stated loss), so a body that is one is returned as it is and orders by nothing.
    /// </summary>
    public static IReadOnlyList<QueryInstruction> Outermost(IReadOnlyList<QueryInstruction> body)
    {
        while (body.Count == 1 && body[0] is SubQueryInstruction nested)
        {
            body = nested.Instructions;
        }

        return body;
    }

    /// <summary>
    /// The ordering key of the outermost query over the fields of its result (decision 170):
    /// for every key of its ordering, in order, the field the key falls on - the alias of a
    /// projection, a projection whose expression equals the key's, or for a query that returns
    /// the whole row the column the key names -, each field once. A key that falls on no field
    /// makes the segments invisible in the result, and the key is then the whole row, every
    /// field; a query without an ordering has an empty key.
    /// </summary>
    public static IReadOnlyList<string> Of(IReadOnlyList<QueryInstruction> body, IReadOnlyList<string> fields)
    {
        var scope = Outermost(body);
        var ordering = scope.OfType<OrderByInstruction>().ToList();
        if (ordering.Count == 0)
        {
            return [];
        }

        var projection = scope.OfType<ProjectInstruction>().Where(item => !item.IsWholeRow).ToList();
        var key = new List<string>();
        foreach (var instruction in ordering)
        {
            var field = FieldOf(instruction.Operand, projection, fields);
            if (field is null)
            {
                return [.. fields];
            }

            if (!key.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                key.Add(field);
            }
        }

        return key;
    }

    /// <summary>
    /// Every scope of a query - the body, the subqueries in its operands, the members of its
    /// set operations, the bodies of its definitions - and the scopes inside those.
    /// </summary>
    public static IEnumerable<IReadOnlyList<QueryInstruction>> Scopes(IReadOnlyList<QueryInstruction> body)
    {
        yield return body;

        foreach (var instruction in body)
        {
            IEnumerable<IReadOnlyList<QueryInstruction>> inner = instruction switch
            {
                SubQueryInstruction nested => Scopes(nested.Instructions),
                WithInstruction definition => Scopes(definition.Body.Instructions),
                SetOperationInstruction operation => Scopes(operation.Left.Instructions).Concat(Scopes(operation.Right.Instructions)),
                _ => OperandsOf(instruction).SelectMany(ScopesOf),
            };

            foreach (var scope in inner)
            {
                yield return scope;
            }
        }
    }

    /// <summary>
    /// Whether a scope under a slice determines the rows it returns, by the sufficient condition
    /// of decision 170: (a) it is an aggregate without a grouping and has one row; (b) its
    /// ordering contains every key of its grouping; or (c) its ordering contains every operand of
    /// its projection. Without a slice the question does not arise and the answer is yes. A
    /// projection of the whole row under a slice is not read, because no ordering could name
    /// its columns from the query alone.
    /// </summary>
    public static bool SliceDeterminesItsRows(IReadOnlyList<QueryInstruction> scope)
    {
        if (!scope.OfType<PaginationInstruction>().Any(slice => slice.Limit is not null || slice.Offset is not null))
        {
            return true;
        }

        var projection = scope.OfType<ProjectInstruction>().ToList();
        if (projection.Count == 0 || projection.Any(item => item.IsWholeRow))
        {
            return false;
        }

        var grouping = scope.OfType<GroupByInstruction>().Select(group => group.Key).ToList();
        if (grouping.Count == 0 && projection.All(item => Aggregated(item.Operand)))
        {
            return true;
        }

        var ordering = scope.OfType<OrderByInstruction>()
            .Select(instruction => Resolved(instruction.Operand, projection))
            .Select(Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return grouping.Count > 0
            ? grouping.All(key => ordering.Contains(Text(key)))
            : projection.All(item => ordering.Contains(Text(item.Operand)));
    }

    private static string? FieldOf(QueryOperand operand, List<ProjectInstruction> projection, IReadOnlyList<string> fields)
    {
        // A query that returns the whole row: the key names a column of it, or nothing of it.
        if (projection.Count == 0)
        {
            return operand is { IsColumn: true, IsAggregate: false } ? Named(operand.Property!, fields) : null;
        }

        if (operand is { IsColumn: true, Table: null, IsAggregate: false }
            && projection.FirstOrDefault(item => item.Alias is not null && string.Equals(item.Alias, operand.Property, StringComparison.OrdinalIgnoreCase)) is { } byAlias)
        {
            return Named(byAlias.Alias!, fields);
        }

        var text = Text(operand);
        var byExpression = projection.FirstOrDefault(item => string.Equals(Text(item.Operand), text, StringComparison.OrdinalIgnoreCase));
        return byExpression is null ? null : Named(byExpression.Alias ?? byExpression.Operand.Property ?? string.Empty, fields);
    }

    private static string? Named(string name, IReadOnlyList<string> fields)
        => fields.FirstOrDefault(field => string.Equals(field, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>An ordering key that names the alias of a projection stands for the projection's operand.</summary>
    private static QueryOperand Resolved(QueryOperand operand, List<ProjectInstruction> projection)
        => operand is { IsColumn: true, Table: null, IsAggregate: false }
            && projection.FirstOrDefault(item => item.Alias is not null && string.Equals(item.Alias, operand.Property, StringComparison.OrdinalIgnoreCase)) is { } aliased
            ? aliased.Operand
            : operand;

    private static bool Aggregated(QueryOperand operand)
        => operand.IsAggregate || (operand.IsExpression && operand.Expression!.Leaves().All(Aggregated));

    /// <summary>
    /// An operand as text, for two operands to be the same expression when they read alike. A
    /// count of the whole row is the same count whichever row source a parser hung it on: HQL
    /// writes count(*) with the alias of its source and T-SQL without one.
    /// </summary>
    private static string Text(QueryOperand operand)
        => operand is { IsColumn: true, Property: "*" }
            ? operand.Function is null ? "*" : $"{operand.Function}({(operand.Distinct ? "DISTINCT " : string.Empty)}*)"
            : operand.ToString();

    private static IEnumerable<QueryOperand> OperandsOf(QueryInstruction instruction) => instruction switch
    {
        ProjectInstruction project => [project.Operand],
        SelectInstruction select => OperandsOf(select.Condition),
        HavingInstruction having => OperandsOf(having.Condition),
        JoinInstruction join => OperandsOf(join.OnCondition),
        GroupByInstruction group => [group.Key],
        OrderByInstruction order => [order.Operand],
        _ => [],
    };

    private static IEnumerable<QueryOperand> OperandsOf(ConditionNode condition) => condition switch
    {
        ComparisonCondition comparison => comparison.Right is null ? [comparison.Left] : [comparison.Left, comparison.Right],
        LogicalCondition logical => logical.Operands.SelectMany(OperandsOf),
        NotCondition not => OperandsOf(not.Operand),
        _ => [],
    };

    private static IEnumerable<IReadOnlyList<QueryInstruction>> ScopesOf(QueryOperand operand)
    {
        if (operand.IsSubQuery)
        {
            return Scopes(operand.SubQuery!.Instructions);
        }

        if (operand.IsExpression)
        {
            return operand.Expression!.Leaves().SelectMany(ScopesOf)
                .Concat(operand.Expression.Branches?.SelectMany(branch => OperandsOf(branch.When).SelectMany(ScopesOf)) ?? []);
        }

        if (operand.IsValueList)
        {
            return operand.Values!.SelectMany(ScopesOf);
        }

        return [];
    }
}
