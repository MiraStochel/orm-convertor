using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// The lexical scope a LINQ chain is being written in. A LINQ lambda names one parameter,
/// and what that parameter holds changes as the chain grows: the source row at first, a
/// transparent tuple after a join, a grouping after GroupBy. Rendering a column therefore
/// needs this state, which is why the LINQ visitor carries it and the SQL one does not.
/// </summary>
public sealed class LinqScope
{
    /// <summary>Name of the current lambda parameter.</summary>
    public string Param { get; set; } = "c";

    /// <summary>True once a join has made the parameter hold a tuple of rows.</summary>
    public bool Composite { get; set; }

    public bool Grouped { get; set; }

    public IReadOnlyList<GroupByInstruction> GroupKeys { get; set; } = [];

    /// <summary>Parameter used inside an aggregate lambda, which ranges over group elements.</summary>
    public string ElementParam { get; set; } = "e";

    public Dictionary<string, EntityMap> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aliases this scope itself declares - the source and each join, mapped or not. What a
    /// nested subquery's scope does not declare it looks up in the enclosing scope, which is
    /// how a correlated reference finds the outer lambda's parameter (decision 061).
    /// </summary>
    public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Row(string? alias) => Composite && alias is not null ? $"{Param}.{alias}" : Param;

    public string ElementRow(string? alias) => Composite && alias is not null ? $"{ElementParam}.{alias}" : ElementParam;
}

/// <summary>
/// Writes query instructions as LINQ (decision 022). Unlike the SQL visitor this one is
/// stateful: it reads <see cref="LinqScope"/> to know what the current lambda parameter
/// stands for.
/// </summary>
public sealed class EFCoreLinqQueryVisitor(
    LinqScope scope,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    EFCoreLinqQueryVisitor? outer = null)
    : IQueryVisitor
{
    public LinqScope Scope { get; } = scope;

    /// <summary>Whether the alias belongs to this scope or one enclosing it (decision 061).</summary>
    public bool Knows(string alias) => Scope.Aliases.Contains(alias) || outer?.Knows(alias) == true;

    public string Visit(FromInstruction instr) => Scope.Param;

    public string Visit(ProjectInstruction instr)
        => Column(instr.Table, instr.Attribute, instr.Function);

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(GroupByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(OrderByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(JoinInstruction instr) => instr.OnCondition.Accept(this);

    /// <summary>
    /// The set operations LINQ names, exhaustively. ExceptAll has no LINQ method and used to
    /// fall into the Except branch, which deduplicates rows the source keeps - a silent change
    /// of meaning of exactly the kind decision 004 forbids, closed here by decision 053.
    /// </summary>
    public string Visit(SetOperationInstruction instr)
    {
        switch (instr.OperationType)
        {
            case SetOperationType.Union: return "Union";
            case SetOperationType.UnionAll: return "Concat";
            case SetOperationType.Intersect: return "Intersect";
            case SetOperationType.Except: return "Except";
            default:
                report(
                    ConversionRecordKind.Failure,
                    $"The set operation {instr.OperationType} has no LINQ form; the query was not generated.",
                    QueryFeature.SetOperation);
                return string.Empty;
        }
    }

    public string Visit(ComparisonCondition cond)
    {
        // EXISTS carries its subquery as the left operand, the way IS NULL carries its
        // column (decisions 002 and 061). LINQ's EXISTS is Any() on the nested chain; the
        // chain itself is the builder's to render, and null means it refused.
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"{sub}.Any()";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            return SubQueryComparison(cond);
        }

        var left = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} == null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} != null";
        }

        if (cond.Right is null)
        {
            // Unreachable: the template refuses such a tree before any step runs
            // (decision 053). Reported rather than substituted, because a tautology in
            // place of a filter returns rows the source excluded.
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        if (cond.Operator == ComparisonOperator.Like)
        {
            return Like(left, cond.Right);
        }

        if (cond.Operator == ComparisonOperator.In)
        {
            // Unreachable: an IN whose right side is not a subquery is refused by the
            // template's gate - the model carries no list of values (decision 061).
            report(ConversionRecordKind.Failure, "An IN whose right side is not a subquery has no LINQ form the query representation can carry; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {Operand(cond.Right)}";
    }

    /// <summary>
    /// A comparison one of whose sides is a subquery (decision 061). IN turns around into
    /// Contains on the nested chain; the scalar operators compare against the chain's
    /// terminal aggregate.
    /// </summary>
    private string SubQueryComparison(ComparisonCondition cond)
    {
        if (cond.Operator == ComparisonOperator.In)
        {
            var values = renderSubQuery(cond.Right!.SubQuery!, cond.Operator);
            var element = OperandOrSubQuery(cond.Left);
            return values is null || element is null ? string.Empty : $"{values}.Contains({element})";
        }

        var left = OperandOrSubQuery(cond.Left);
        var right = OperandOrSubQuery(cond.Right!);
        if (left is null || right is null)
        {
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {right}";
    }

    /// <summary>
    /// The operand's text, rendering a subquery in a scalar position - which is what a
    /// subquery standing anywhere but as IN's right side is, whatever the operator around
    /// it says.
    /// </summary>
    private string? OperandOrSubQuery(QueryOperand operand)
        => operand.IsSubQuery ? renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal) : Operand(operand);

    /// <summary>
    /// A LIKE pattern in LINQ (decision 051). Where the pattern is anchored only at its ends
    /// and its core carries no wildcard, the exact LINQ counterpart exists and is written:
    /// <c>%x%</c> is Contains, <c>x%</c> StartsWith, <c>%x</c> EndsWith and a pattern without
    /// wildcards is equality. Anything else - a wildcard in the middle, an underscore, a
    /// character class, or a right side that is not a literal at all - goes out as
    /// EF.Functions.Like, which EF Core translates to LIKE unchanged. Handing the pattern to
    /// Contains verbatim, as this used to, searched for literal percent signs.
    /// </summary>
    private string Like(string left, QueryOperand right)
    {
        var literalPattern = right.IsConstant && right.Function is null ? right.Constant!.Text : null;

        if (literalPattern is not null && TryReadPattern(literalPattern, out var method, out var core))
        {
            return method is null
                ? $"{left} == {StringLiteral(core)}"
                : $"{left}.{method}({StringLiteral(core)})";
        }

        // A LIKE pattern is a string whatever scalar the parser managed to put on it, so a
        // constant goes out quoted rather than through the general literal rendering, which
        // would spell an untyped one bare and the result would not compile.
        var pattern = literalPattern is not null ? StringLiteral(literalPattern) : Operand(right);

        return $"EF.Functions.Like({left}, {pattern})";
    }

    /// <summary>
    /// Splits a LIKE pattern into its anchors and its core, or refuses. The core has to be
    /// free of every wildcard: EF Core escapes the argument of Contains and friends, so a
    /// core holding <c>_</c> would come out matching a literal underscore where the source
    /// matched any character.
    /// </summary>
    /// <param name="method">The string method to call, or null for plain equality.</param>
    private static bool TryReadPattern(string pattern, out string? method, out string core)
    {
        method = null;
        core = pattern;

        var leading = pattern.StartsWith('%');
        var trailing = pattern.Length > (leading ? 1 : 0) && pattern.EndsWith('%');

        core = pattern[(leading ? 1 : 0)..(pattern.Length - (trailing ? 1 : 0))];

        if (core.AsSpan().IndexOfAny('%', '_', '[') >= 0)
        {
            return false;
        }

        method = (leading, trailing) switch
        {
            (true, true) => "Contains",
            (false, true) => "StartsWith",
            (true, false) => "EndsWith",
            _ => null,
        };

        return true;
    }

    private static string StringLiteral(string text)
        => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    public string Visit(LogicalCondition cond)
    {
        var keyword = cond.Operator == LogicalOperator.And ? "&&" : "||";

        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition
                ? $"({operand.Accept(this)})"
                : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"!({cond.Operand.Accept(this)})";

    /// <summary>
    /// The relational operators, exhaustively. No catch-all branch: a value the target has
    /// no form for used to come out as the neighbouring operator, which is a silent change
    /// of meaning rather than a loss (decision 053). Like, In and the null tests never reach
    /// here - they have their own shapes above.
    /// </summary>
    private string Operator(ComparisonOperator op)
    {
        switch (op)
        {
            case ComparisonOperator.Equal: return "==";
            case ComparisonOperator.NotEqual: return "!=";
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.GreaterThanOrEqual: return ">=";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.LessThanOrEqual: return "<=";
            default:
                report(ConversionRecordKind.Failure, $"Operator {op} has no LINQ form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    public string Operand(QueryOperand operand)
        => operand.IsConstant ? Literal(operand.Constant!) : Column(operand.Table, operand.Property!, operand.Function);

    /// <summary>
    /// Renders a column reference in the current scope: a plain member access, a group key,
    /// or an aggregate over the group's elements.
    /// </summary>
    public string Column(string? alias, string attribute, string? function)
    {
        // A reference this scope does not declare but an enclosing one does is a correlated
        // reference: it renders through the outer visitor, whose lambda parameter is still
        // in scope inside the nested chain (decision 061).
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.Column(alias, attribute, function);
        }

        if (function is not null)
        {
            return Aggregate(alias, attribute, function);
        }

        if (Scope.Grouped)
        {
            var key = GroupKeyPath(alias, attribute);
            if (key is not null)
            {
                return key;
            }

            report(
                ConversionRecordKind.Loss,
                $"Column {attribute} is neither a grouping key nor an aggregate, so it cannot be read after GroupBy; it was dropped.",
                QueryFeature.Projection);
            return $"{Scope.Param}.Key";
        }

        return $"{Scope.Row(alias)}.{Property(alias, attribute)}";
    }

    private string Aggregate(string? alias, string attribute, string function)
    {
        if (!Scope.Grouped)
        {
            report(
                ConversionRecordKind.Loss,
                $"{function} appears without a grouping, which LINQ cannot express inside a query; the aggregate was dropped.",
                QueryFeature.Aggregation);
            return $"{Scope.Row(alias)}.{Property(alias, attribute)}";
        }

        if (function == "COUNT")
        {
            if (attribute != "*")
            {
                report(
                    ConversionRecordKind.Convention,
                    $"COUNT({attribute}) was written as Count(), which counts rows rather than non-null values.",
                    QueryFeature.Aggregation);
            }

            return $"{Scope.Param}.Count()";
        }

        var method = function switch
        {
            "SUM" => "Sum",
            "MIN" => "Min",
            "MAX" => "Max",
            "AVG" => "Average",
            _ => null,
        };

        if (method is null)
        {
            report(
                ConversionRecordKind.Loss,
                $"Aggregate function {function} has no LINQ counterpart; it was dropped.",
                QueryFeature.Aggregation);
            return $"{Scope.Param}.Count()";
        }

        return $"{Scope.Param}.{method}({Scope.ElementParam} => {Scope.ElementRow(alias)}.{Property(alias, attribute)})";
    }

    /// <summary>
    /// The path to a grouping key, or null when the column is not one. A single key is
    /// reached through Key itself; a composite key through a member of it.
    /// </summary>
    private string? GroupKeyPath(string? alias, string attribute)
    {
        var index = -1;
        for (int i = 0; i < Scope.GroupKeys.Count; i++)
        {
            if (string.Equals(Scope.GroupKeys[i].Attribute, attribute, StringComparison.OrdinalIgnoreCase)
                && (alias is null || string.Equals(Scope.GroupKeys[i].Table, alias, StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        return Scope.GroupKeys.Count == 1
            ? $"{Scope.Param}.Key"
            : $"{Scope.Param}.Key.{Property(Scope.GroupKeys[index].Table, Scope.GroupKeys[index].Attribute)}";
    }

    public string Property(string? alias, string column)
    {
        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return map?.PropertyMaps
                   .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
                   ?.Property.Name
               ?? column;
    }

    /// <summary>
    /// Writes a constant the way C# wants it (decision 024). The model carries the value
    /// undecorated, so the quoting and the numeric suffix are added here from the scalar
    /// type rather than carried over from whatever the source language wrote.
    /// </summary>
    private string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String => StringLiteral(constant.Text),
        ScalarType.Char => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        ScalarType.Long => constant.Text + "L",
        ScalarType.Decimal => constant.Text + "m",
        ScalarType.Double => constant.Text + "d",
        ScalarType.Float => constant.Text + "f",
        ScalarType.DateTime => $"DateTime.Parse(\"{constant.Text}\")",
        ScalarType.Guid => $"Guid.Parse(\"{constant.Text}\")",
        _ => constant.Text,
    };
}
