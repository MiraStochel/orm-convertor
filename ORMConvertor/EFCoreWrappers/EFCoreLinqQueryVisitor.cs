using Common.Convertors;
using Common.Naming;
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
        => Column(instr.Table, instr.Attribute, instr.Function, instr.Distinct);

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
            return Like(left, cond.Right, cond.Escape);
        }

        if (cond.Operator == ComparisonOperator.In)
        {
            // IN over enumerated values (decision 074) turns around into Contains on an
            // inline array of constants, which EF Core translates back to IN (...). The
            // subquery right side went through SubQueryComparison above; anything else is
            // unreachable, because the template's gate refuses it (decisions 061 and 074).
            if (cond.Right.IsValueList)
            {
                return $"{ValueList(cond.Right, NullableElementType(cond.Left))}.Contains({left})";
            }

            // A collection parameter turns around the same way (decision 083): the sequence
            // the caller binds is the receiver, which is the shape the source wrote in the
            // first place when the source was LINQ. The sequence is declared over the plain
            // scalar (decision 083), and IEnumerable<int> has no Contains that takes an
            // int?, so a nullable column offers its value - which EF Core translates as the
            // column itself, and IN never matches a NULL anyway.
            if (cond.Right.IsParameter && cond.Right.Parameter!.IsCollection)
            {
                var member = NullableElementType(cond.Left) is null ? left : $"{left}.Value";
                return $"{QueryParameterNaming.IdentifierFor(cond.Right.Parameter!)}.Contains({member})";
            }

            report(ConversionRecordKind.Failure, "An IN whose right side is neither a subquery, a list of values nor a collection parameter has no LINQ form; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {Operand(cond.Right)}";
    }

    /// <summary>
    /// The inline array of an IN list (decision 074). Its element type is inferred from the
    /// literals unless the column it is compared with is a nullable value type: int[] has
    /// no Contains that takes an int?, so against such a column the array declares the
    /// nullable element type and the artifact compiles - which the inferred form did not.
    /// </summary>
    private string ValueList(QueryOperand operand, string? elementType = null)
        => $"new{(elementType is null ? string.Empty : " " + elementType)}[] {{ {string.Join(", ", operand.Values!.Select(Operand))} }}";

    /// <summary>
    /// The C# spelling of the column's type when the mapping makes it a nullable value
    /// type (<c>int?</c>), which is the one case an IN over it has to be typed for; null
    /// for a column that is not mapped, not nullable, or of a reference type, where the
    /// inferred element type is the right one already.
    /// </summary>
    private string? NullableElementType(QueryOperand operand)
    {
        if (operand.IsParameter || operand.IsConstant || operand.IsValueList || operand.IsSubQuery || operand.Function is not null)
        {
            return null;
        }

        var type = PropertyType(operand.Table, operand.Property!);
        if (type?.ScalarType is not { } scalar || !type.IsNullable || scalar is ScalarType.String or ScalarType.Object or ScalarType.ByteArray)
        {
            return null;
        }

        return CSharpTypeConvertor.ToString(LangType.Scalar(scalar)) + "?";
    }

    /// <summary>The language type the mapping gives a column of the scope, or of an enclosing scope for a correlated reference; null where nothing maps it.</summary>
    private LangType? PropertyType(string? alias, string column)
    {
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.PropertyType(alias, column);
        }

        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return map?.PropertyMaps
            .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
            ?.Property.Type;
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
    ///
    /// With an escape character (decision 102) the pattern is read past it: a wildcard
    /// behind the escape is a literal character, the core goes out without the escapes -
    /// EF Core escapes the argument of a string method itself, so <c>'A!_%' ESCAPE '!'</c>
    /// is <c>StartsWith("A_")</c> - and where the split is not exact, the escape travels
    /// as the third argument of EF.Functions.Like.
    /// </summary>
    private string Like(string left, QueryOperand right, string? escape)
    {
        var literalPattern = right.IsConstant && right.Function is null ? right.Constant!.Text : null;

        if (literalPattern is not null && TryReadPattern(literalPattern, escape, out var method, out var core))
        {
            return method is null
                ? $"{left} == {StringLiteral(core)}"
                : $"{left}.{method}({StringLiteral(core)})";
        }

        // A LIKE pattern is a string whatever scalar the parser managed to put on it, so a
        // constant goes out quoted rather than through the general literal rendering, which
        // would spell an untyped one bare and the result would not compile.
        var pattern = literalPattern is not null ? StringLiteral(literalPattern) : Operand(right);

        return escape is null
            ? $"EF.Functions.Like({left}, {pattern})"
            : $"EF.Functions.Like({left}, {pattern}, {StringLiteral(escape)})";
    }

    /// <summary>
    /// Splits a LIKE pattern into its anchors and its core, or refuses. The core has to be
    /// free of every wildcard: EF Core escapes the argument of Contains and friends, so a
    /// core holding <c>_</c> would come out matching a literal underscore where the source
    /// matched any character. A character behind the escape (decision 102) is no wildcard
    /// and reaches the core without its escape; an escape character with nothing behind it
    /// refuses the split, so that the text goes out as it was written.
    /// </summary>
    /// <param name="method">The string method to call, or null for plain equality.</param>
    private static bool TryReadPattern(string pattern, string? escape, out string? method, out string core)
    {
        method = null;
        core = pattern;

        var characters = new List<(char Value, bool Literal)>(pattern.Length);
        for (var i = 0; i < pattern.Length; i++)
        {
            if (escape is not null && pattern[i] == escape[0])
            {
                if (i + 1 >= pattern.Length)
                {
                    return false;
                }

                characters.Add((pattern[++i], true));
                continue;
            }

            characters.Add((pattern[i], false));
        }

        var leading = characters.Count > 0 && characters[0] is ('%', false);
        var trailing = characters.Count > (leading ? 1 : 0) && characters[^1] is ('%', false);

        var inner = characters[(leading ? 1 : 0)..(characters.Count - (trailing ? 1 : 0))];

        if (inner.Any(c => !c.Literal && c.Value is '%' or '_' or '['))
        {
            return false;
        }

        core = new string(inner.Select(c => c.Value).ToArray());

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
        => operand.IsValueList
            ? ValueList(operand)
            : operand.IsParameter
                // LINQ has no placeholder: the parameter of the generated method is captured
                // by the lambda and written under its own name (decision 083).
                ? QueryParameterNaming.IdentifierFor(operand.Parameter!)
                : operand.IsConstant
                    ? Literal(operand.Constant!)
                    : Column(operand.Table, operand.Property!, operand.Function, operand.Distinct);

    /// <summary>
    /// Renders a column reference in the current scope: a plain member access, a group key,
    /// or an aggregate over the group's elements - over their distinct values when
    /// <paramref name="distinct"/> says so (decision 102).
    /// </summary>
    public string Column(string? alias, string attribute, string? function, bool distinct = false)
    {
        // A reference this scope does not declare but an enclosing one does is a correlated
        // reference: it renders through the outer visitor, whose lambda parameter is still
        // in scope inside the nested chain (decision 061).
        if (alias is not null && outer is not null && !Scope.Aliases.Contains(alias) && outer.Knows(alias))
        {
            return outer.Column(alias, attribute, function, distinct);
        }

        if (function is not null)
        {
            return Aggregate(alias, attribute, function, distinct);
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

    private string Aggregate(string? alias, string attribute, string function, bool distinct)
    {
        // An aggregate over an ungrouped scope has no place inside a LINQ projection: the
        // chain must end in the aggregate call, which is not the IQueryable this builder
        // emits. Writing the bare column instead answers with every row where the source
        // answered with one number - a different result, not a poorer one (decision 053) -
        // and over COUNT(*) it wrote `c.*`, which is not even valid C#. The same shape inside
        // a subquery operand is refused by RenderSubQuery for the same reason.
        if (!Scope.Grouped)
        {
            report(
                ConversionRecordKind.Failure,
                $"{function} is projected without a grouping, which a LINQ chain can only express by ending in the aggregate call rather than by a query; no artifact was generated.",
                QueryFeature.Aggregation);
            return string.Empty;
        }

        if (function == "COUNT" && !distinct)
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
            "COUNT" => "Count",
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

        var element = $"{Scope.ElementParam} => {Scope.ElementRow(alias)}.{Property(alias, attribute)}";

        // The aggregate over the distinct values of the column (decision 102): a Select of
        // the column, collapsed, then the parameterless aggregate - the shape EF Core
        // translates to COUNT(DISTINCT ...). It counts distinct non-null values, exactly as
        // COUNT(DISTINCT x) does, so unlike Count() it needs no record.
        if (distinct)
        {
            return $"{Scope.Param}.Select({element}).Distinct().{method}()";
        }

        return $"{Scope.Param}.{method}({element})";
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
        // The same shape for the temporal scalars of decision 071: a parsed constant EF
        // Core evaluates on the client and sends as a parameter, like DateTime above.
        ScalarType.Date => $"DateOnly.Parse(\"{constant.Text}\")",
        ScalarType.TimeOfDay => $"TimeOnly.Parse(\"{constant.Text}\")",
        ScalarType.DateTimeOffset => $"DateTimeOffset.Parse(\"{constant.Text}\")",
        ScalarType.Duration => $"TimeSpan.Parse(\"{constant.Text}\")",
        _ => constant.Text,
    };
}
