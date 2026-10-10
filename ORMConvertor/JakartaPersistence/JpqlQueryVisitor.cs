using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace JakartaPersistence;

/// <summary>
/// Writes query instructions as JPQL (decision 077). Shaped like the HQL visitor of
/// NHibernate - JPQL names entities and attributes rather than tables and columns, so
/// every reference goes through the mapping IR - and standard where JPQL 3.2 has a form:
/// temporal literals in the JDBC escape syntax the specification prescribes, count over
/// the alias instead of count(*). The entity join with on is the one form outside the
/// standard, which both implementations add (decision 077).
/// </summary>
/// <param name="typing">The typed view of the query's expressions the builder's gate filled (decision 107): which <c>+</c> stands over a string, because JPQL spells a concatenation with a word of its own.</param>
/// <param name="intermediate">The aliases whose rows are an intermediate result of the query (decision 112), over which HQL counts with <c>count(*)</c>: a derived row has no identity for <c>count(d)</c> to count.</param>
/// <param name="profile">The implementation the JPQL is written for, where its spelling of a construct is measured to differ (<see cref="JpaImplementationProfile.KeyThroughReferenceJoins"/>).</param>
public sealed class JpqlQueryVisitor(
    Dictionary<string, EntityMap> entities,
    string sourceAlias,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing,
    IReadOnlySet<string>? intermediate = null,
    JpaImplementationProfile? profile = null) : IQueryVisitor
{
    /// <summary>The escape character of the like whose pattern is being written (decision 107); null outside a pattern.</summary>
    private string? patternEscape;

    public string Visit(FromInstruction instr) => instr.Alias ?? instr.Table;

    /// <summary>
    /// A projected value under its result variable. EclipseLink 5.0.0 refuses a reserved
    /// identifier there as well (<c>select count(o) as count</c>, <c>… as value</c>, verified),
    /// so the result variable is respelled like an identification variable
    /// (<see cref="JpqlNames"/>), and so is every name that refers to it - an ordering by it,
    /// a column of the intermediate result it names. The result is read by position, so the
    /// respelling changes no shape the caller sees (decision 104).
    /// </summary>
    public string Visit(ProjectInstruction instr)
    {
        var value = Operand(instr.Operand);
        return instr.Alias is null ? value : $"{value} as {JpqlNames.Alias(instr.Alias)}";
    }

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    /// <summary>A grouping key (decision 113): a path, or an expression, which Hibernate groups by as written and EclipseLink as well where its descriptor says so.</summary>
    public string Visit(GroupByInstruction instr) => Operand(instr.Key);

    public string Visit(OrderByInstruction instr)
        => $"{Operand(instr.Operand)} {(instr.Asc ? "asc" : "desc")}";

    public string Visit(JoinInstruction instr)
    {
        var keyword = instr.Kind switch
        {
            JoinKind.Inner => "join",
            JoinKind.Left => "left join",
            JoinKind.Right => "right join",
            JoinKind.Full => "full join",
            _ => throw new ArgumentOutOfRangeException(nameof(instr), instr.Kind, null),
        };

        // A table no entity maps to takes the name the one naming convention derives
        // (decision 050), as the source step does for the from clause.
        var entity = EntityName(instr.RightTableAlias ?? instr.RightTable) ?? EntityTableNaming.EntityNameFor(instr.RightTable);
        var alias = JpqlNames.Alias(instr.RightTableAlias ?? entity.ToLowerInvariant());

        // JPQL has no other spelling of an entity name than the name itself, so a name the
        // implementation's parser refuses as a join target leaves the query to native SQL,
        // which names the table (decision 113, measured).
        if (profile is not null
            && (profile.EntityNamesRefused?.Contains(entity) == true || profile.EntityNamesRefusedAsJoinTarget?.Contains(entity) == true))
        {
            report(
                ConversionRecordKind.Fallback,
                $"JPQL in {profile.Implementation} does not read '{entity}', which is spelled like a word of its grammar, as the entity of a join",
                QueryFeature.Join);
        }

        // A condition of several conjuncts goes in parentheses: EclipseLink 5.0 reads an
        // unparenthesized `on a = b and c = d join …` on to the next clause instead of
        // stopping at the next join and refuses it as "the right expression is not a valid
        // expression", whereas the parenthesized form is the same JPQL expression and
        // Hibernate reads it as well. Found by the Java suite over the deeply nested query.
        var condition = JoinCondition(instr.OnCondition);

        return $"{keyword} {entity} {alias} on {condition}";
    }

    /// <summary>
    /// The condition of an entity join, in parentheses where it has several conjuncts. The
    /// equalities that say an owning reference points at the other row are written as the
    /// reference compared with the row (<see cref="ColumnMember.ReferenceComparisons"/>):
    /// both implementations read <c>p.customer = c</c> off the foreign key columns, a
    /// composite key included, where EclipseLink 5.0.0 reaches <c>p.customer.id</c> with a
    /// join it cannot write inside the on of an outer join (verified).
    /// </summary>
    private string JoinCondition(ConditionNode condition)
    {
        var references = ColumnMember.ReferenceComparisons(condition, entities);
        if (references.Count == 0)
        {
            return condition is LogicalCondition ? $"({condition.Accept(this)})" : condition.Accept(this);
        }

        var parts = new List<string>();
        foreach (var conjunct in ColumnMember.Conjuncts(condition))
        {
            if (references.FirstOrDefault(r => r.StandsFor(conjunct)) is { } reference)
            {
                if (ReferenceEquals(reference.Conjuncts[0], conjunct))
                {
                    parts.Add($"{JpqlNames.Alias(reference.Alias)}.{reference.Navigation} = {JpqlNames.Alias(reference.Target)}");
                }

                continue;
            }

            parts.Add(conjunct is LogicalCondition ? $"({conjunct.Accept(this)})" : conjunct.Accept(this));
        }

        return parts.Count == 1 ? parts[0] : $"({string.Join(" and ", parts)})";
    }

    public string Visit(SetOperationInstruction instr) => string.Empty; // composed by the builder

    public string Visit(ComparisonCondition cond)
    {
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"exists ({sub})";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            var left = OperandOrSubQuery(cond.Left, cond.Operator);
            var right = OperandOrSubQuery(cond.Right!, cond.Operator);

            // JPQL spells the quantifier of decision 119 as SQL does: x > all (select …).
            var quantifier = cond.Quantifier is { } q ? QuantifiedComparisons.Spelled(q).ToLowerInvariant() + " " : string.Empty;
            return left is null || right is null ? string.Empty : $"{left} {Operator(cond.Operator)} {quantifier}{right}";
        }

        var operand = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{operand} is null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{operand} is not null";
        }

        if (cond.Right is null)
        {
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        patternEscape = cond.Operator == ComparisonOperator.Like ? cond.Escape : null;
        var rightText = Operand(cond.Right);
        patternEscape = null;

        return $"{operand} {Operator(cond.Operator)} {rightText}{Escape(cond)}";
    }

    /// <summary>The escape clause of a like (decision 102); the template holds it to like and to one character.</summary>
    private static string Escape(ComparisonCondition cond)
        => cond.Escape is null ? string.Empty : $" escape '{cond.Escape.Replace("'", "''")}'";

    private string? OperandOrSubQuery(QueryOperand operand, ComparisonOperator op)
    {
        if (!operand.IsSubQuery)
        {
            return Operand(operand);
        }

        var sub = renderSubQuery(operand.SubQuery!, op);
        return sub is null ? null : $"({sub})";
    }

    public string Visit(LogicalCondition cond)
    {
        var keyword = cond.Operator == LogicalOperator.And ? "and" : "or";
        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition ? $"({operand.Accept(this)})" : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"not ({cond.Operand.Accept(this)})";

    private string Operator(ComparisonOperator op)
    {
        switch (op)
        {
            case ComparisonOperator.Equal: return "=";
            case ComparisonOperator.NotEqual: return "<>";
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.GreaterThanOrEqual: return ">=";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.LessThanOrEqual: return "<=";
            case ComparisonOperator.Like: return "like";
            case ComparisonOperator.In: return "in";
            default:
                report(ConversionRecordKind.Failure, $"Operator {op} has no JPQL form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    private string Operand(QueryOperand operand)
    {
        if (operand.IsValueList)
        {
            return $"({string.Join(", ", operand.Values!.Select(Operand))})";
        }

        if (operand.IsParameter)
        {
            return Parameter(operand.Parameter!, operand.Function);
        }

        if (operand.IsConstant)
        {
            return Wrap(Literal(operand.Constant!), operand.Function);
        }

        // A subquery in a scalar position - a leaf of an expression (decision 107).
        if (operand.IsSubQuery)
        {
            var sub = renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal);
            return sub is null ? string.Empty : $"({sub})";
        }

        if (operand.IsExpression)
        {
            return Wrap(Expression(operand.Expression!), operand.Function, operand.Distinct);
        }

        return Column(operand.Table, operand.Property!, operand.Function, operand.Distinct);
    }

    /// <summary>
    /// A parameter in JPQL (decision 083). The one target language with a positional form,
    /// so a positional parameter keeps its order here instead of being renamed. A collection
    /// parameter is written without parentheses: the grammar of Jakarta Persistence 3.2
    /// puts a collection-valued input parameter in IN's place itself, and parentheses there
    /// would make it one item of a list.
    /// </summary>
    private static string Parameter(QueryParameter parameter, string? function)
    {
        var placeholder = parameter.IsPositional
            ? $"?{parameter.Position}"
            : $":{parameter.Name}";

        return parameter.IsCollection ? placeholder : Wrap(placeholder, function);
    }

    private static string Wrap(string value, string? function, bool distinct = false)
        => function is null ? value : $"{function.ToLowerInvariant()}({(distinct ? "distinct " : string.Empty)}{value})";

    /// <param name="distinct">Whether the aggregate ranges over the distinct values of the column (decision 102).</param>
    private string Column(string? alias, string attribute, string? function, bool distinct = false)
    {
        // count(*) is not JPQL; the standard counts the identification variable. A row of an
        // intermediate result has no identity to count, so there HQL's count(*) stands - the
        // one target that writes a definition is Hibernate (decision 112).
        if (function is not null && attribute == "*")
        {
            var counted = alias ?? sourceAlias;
            return intermediate?.Contains(counted) == true
                ? $"{function.ToLowerInvariant()}(*)"
                : $"{function.ToLowerInvariant()}({JpqlNames.Alias(counted)})";
        }

        // An unqualified name in JPQL is a result variable, and a column of an intermediate
        // result is the result variable of its definition: both are written as declared.
        var path = alias is null
            ? JpqlNames.Alias(Property(null, attribute))
            : intermediate?.Contains(alias) == true
                ? $"{JpqlNames.Alias(alias)}.{JpqlNames.Alias(Property(alias, attribute))}"
                : $"{JpqlNames.Alias(alias)}.{Property(alias, attribute)}";
        return Wrap(path, function, distinct);
    }

    /// <summary>
    /// The attribute a column is written as: its persistent attribute, or the referenced
    /// identifier through the reference that holds a foreign key column no attribute maps
    /// (<see cref="ColumnMember"/>) - the usual shape in Jakarta Persistence, where the join
    /// column belongs to the reference alone.
    /// </summary>
    public string Property(string? alias, string column)
    {
        var map = alias is not null && entities.TryGetValue(alias, out var found) ? found : null;

        // An implementation that reaches the key through the reference with an inner join of
        // the referenced table drops the rows whose foreign key is NULL, and inside the on of
        // an outer join EclipseLink 5.0.0 writes SQL the database refuses (verified); the
        // native SQL names the column itself (decision 113).
        if (profile?.KeyThroughReferenceJoins == true
            && map is not null
            && ColumnMember.ScalarOf(map, column) is null
            && ColumnMember.HeldBy(map, column) is { } held)
        {
            report(
                ConversionRecordKind.Fallback,
                $"JPQL in {profile.Implementation} reaches the key '{held.Pair.Target.Property.Name}' the reference '{held.Reference.SourceNavigationProperty}' holds as '{column}' only through an inner join of the referenced table, which drops the rows whose foreign key is NULL",
                QueryFeature.Join);
        }

        return ColumnMember.PathOf(map, column) ?? column;
    }

    public string? EntityName(string alias)
        => entities.TryGetValue(alias, out var map) ? map.Entity.Name : null;

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// An expression in the spelling of Jakarta Persistence 3.2 (decision 107):
    /// <c>concat(a, b, …)</c> for a concatenation, flattened; the arithmetic operators and
    /// <c>mod(a, b)</c>; the functions of the vocabulary in lower case, the parts of a date
    /// through the standard <c>extract</c> - not Hibernate's <c>year()</c>, so that EclipseLink
    /// reads it too -, <c>current_timestamp</c> without parentheses; and a searched case,
    /// whose else the grammar demands, so a CASE without one writes <c>else null</c>.
    /// </summary>
    private string Expression(QueryExpression expression)
    {
        // HQL 7.4's ranking functions and listagg (decision 113). Standard JPQL has neither,
        // so EclipseLink's descriptor leaves both out and its query goes to native SQL before
        // a step runs; only Hibernate reaches here.
        if (expression.IsWindow)
        {
            var partitions = expression.Partitions!.Count == 0
                ? string.Empty
                : $"partition by {string.Join(", ", expression.Partitions.Select(Operand))} ";
            var function = expression.Ranking switch
            {
                RankingFunction.RowNumber => "row_number",
                RankingFunction.Rank => "rank",
                RankingFunction.DenseRank => "dense_rank",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Ranking, null),
            };

            return $"{function}() over ({partitions}order by {Ordered(expression.Ordering!)})";
        }

        if (expression.IsListAggregate)
        {
            var within = expression.Ordering!.Count == 0 ? string.Empty : $" within group (order by {Ordered(expression.Ordering)})";
            return $"listagg({Operand(expression.Listed!)}, '{expression.Separator!.Replace("'", "''")}'){within}";
        }

        if (expression.IsBinary)
        {
            if (typing.IsConcatenation(expression))
            {
                return $"concat({string.Join(", ", Concatenated(expression).Select(Operand))})";
            }

            if (expression.Operator == ExpressionOperator.Modulo)
            {
                return $"mod({Operand(expression.Left!)}, {Operand(expression.Right!)})";
            }

            var symbol = expression.Operator!.Value switch
            {
                ExpressionOperator.Add => "+",
                ExpressionOperator.Subtract => "-",
                ExpressionOperator.Multiply => "*",
                ExpressionOperator.Divide => "/",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Operator, null),
            };

            return $"{Side(expression.Left!, expression, rightSide: false)} {symbol} {Side(expression.Right!, expression, rightSide: true)}";
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!.Select(Operand).ToList();
            return expression.Function!.Value switch
            {
                QueryFunction.Upper => $"upper({arguments[0]})",
                QueryFunction.Lower => $"lower({arguments[0]})",
                QueryFunction.Trim => $"trim({arguments[0]})",
                QueryFunction.Substring => $"substring({arguments[0]}, {arguments[1]}, {arguments[2]})",
                QueryFunction.Length => $"length({arguments[0]})",
                QueryFunction.Coalesce => $"coalesce({string.Join(", ", arguments)})",
                QueryFunction.Abs => $"abs({arguments[0]})",
                QueryFunction.Year => $"extract(year from {arguments[0]})",
                QueryFunction.Month => $"extract(month from {arguments[0]})",
                QueryFunction.Day => $"extract(day from {arguments[0]})",
                QueryFunction.CurrentTimestamp => "current_timestamp",
                QueryFunction.EscapePattern => ExpressionSpelling.EscapePattern(arguments[0], patternEscape ?? "!", "replace"),
                QueryFunction.Round => $"round({arguments[0]}, {arguments[1]})",
                QueryFunction.Sqrt => $"sqrt({arguments[0]})",
                QueryFunction.Cast => Cast(expression, arguments[0]),
                QueryFunction.DateAdd => $"timestampadd({Unit(expression.Unit!.Value)}, {arguments[0]}, {arguments[1]})",
                QueryFunction.DateDiff => $"timestampdiff({Unit(expression.Unit!.Value)}, {arguments[0]}, {arguments[1]})",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        var branches = string.Join(" ", expression.Branches!.Select(b => $"when {b.When.Accept(this)} then {Operand(b.Then)}"));
        var otherwise = expression.Else is null ? "null" : Operand(expression.Else);
        return $"case {branches} else {otherwise} end";
    }

    /// <summary>
    /// A conversion (decision 113): <c>cast(x as type)</c> under the type's name in Jakarta
    /// Persistence 3.2. Hibernate 7.4.5 writes the conversion into String as
    /// <c>varchar(max)</c> over SQL Server - verified -, which is the same text as the model's
    /// <c>NVARCHAR(MAX)</c> for a number or a moment converted and a poorer one for a text
    /// holding characters outside the code page; so a conversion into text is written over a
    /// value known not to be text, and over any other the query goes out in native SQL.
    /// EclipseLink's descriptor leaves cast out altogether.
    /// </summary>
    private string Cast(QueryExpression expression, string argument)
    {
        if (expression.CastTo == ScalarType.String && LeafScalar(expression.Arguments![0]) is null or ScalarType.String or ScalarType.Char)
        {
            report(
                ConversionRecordKind.Fallback,
                $"JPQL converts into String as varchar(max), which would lose the characters of the text '{expression.Arguments[0]}' that no code page holds",
                QueryFeature.Expression);
            return string.Empty;
        }

        return $"cast({argument} as {CastType(expression.CastTo!.Value)})";
    }

    /// <summary>
    /// The scalar of a leaf as this visitor can know it: a constant's own, a column's from the
    /// mapping behind its alias, an expression's from the typed view; null for a parameter, a
    /// subquery or an aggregate.
    /// </summary>
    private ScalarType? LeafScalar(QueryOperand operand)
    {
        if (operand.IsAggregate)
        {
            return null;
        }

        if (operand.IsConstant)
        {
            return operand.Constant!.Type;
        }

        if (operand.IsExpression)
        {
            return typing.ScalarOf(operand.Expression!);
        }

        if (operand.IsColumn && operand.Table is not null && entities.TryGetValue(operand.Table, out var map))
        {
            return ColumnMember.TypedBy(map, operand.Property!)?.Property.Type is { Category: LangTypeCategory.Scalar } type
                ? type.ScalarType
                : null;
        }

        return null;
    }

    /// <summary>The keys of a window or of a list aggregate, each with its direction (decision 113).</summary>
    private string Ordered(IReadOnlyList<OrderingKey> ordering)
        => string.Join(", ", ordering.Select(key => $"{Operand(key.Operand)} {(key.Ascending ? "asc" : "desc")}"));

    /// <summary>
    /// The type a conversion names (decision 113): the five names of Jakarta Persistence 3.2,
    /// <c>Integer</c>, <c>Long</c>, <c>Float</c>, <c>Double</c> and <c>String</c>, which both
    /// implementations take.
    /// </summary>
    public static string CastType(ScalarType scalar) => scalar switch
    {
        ScalarType.Int => "Integer",
        ScalarType.Long => "Long",
        ScalarType.Float => "Float",
        ScalarType.Double => "Double",
        ScalarType.String => "String",
        _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar, null),
    };

    /// <summary>The temporal unit of HQL's timestampadd and timestampdiff (decision 113).</summary>
    public static string Unit(DateUnit unit) => unit switch
    {
        DateUnit.Year => "year",
        DateUnit.Month => "month",
        DateUnit.Day => "day",
        DateUnit.Hour => "hour",
        DateUnit.Minute => "minute",
        DateUnit.Second => "second",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The operands of a concatenation, its nested concatenations flattened into one argument list.</summary>
    private IEnumerable<QueryOperand> Concatenated(QueryExpression concatenation)
    {
        foreach (var side in new[] { concatenation.Left!, concatenation.Right! })
        {
            if (side is { IsExpression: true, IsAggregate: false } && side.Expression!.IsBinary && typing.IsConcatenation(side.Expression))
            {
                foreach (var inner in Concatenated(side.Expression))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return side;
            }
        }
    }

    private string Side(QueryOperand side, QueryExpression parent, bool rightSide)
    {
        var text = Operand(side);
        return ExpressionSpelling.NeedsParentheses(side, parent, rightSide) ? $"({text})" : text;
    }

    /// <summary>
    /// A constant the way JPQL wants it (decision 024): strings quoted, numbers bare, and
    /// the temporal families in the JDBC escape syntax the specification names for date,
    /// time and timestamp literals (Jakarta Persistence 3.2 §4.6.1).
    /// </summary>
    public static string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String or ScalarType.Char or ScalarType.Guid => $"'{constant.Text.Replace("'", "''")}'",
        ScalarType.Date => $"{{d '{constant.Text}'}}",
        ScalarType.TimeOfDay => $"{{t '{constant.Text}'}}",
        ScalarType.DateTime => $"{{ts '{constant.Text}'}}",
        ScalarType.DateTimeOffset or ScalarType.Duration => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        _ => constant.Text,
    };
}
