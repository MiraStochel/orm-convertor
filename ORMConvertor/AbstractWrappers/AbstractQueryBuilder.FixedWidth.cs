using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The value of a fixed-width column as the source's program received it (decision 169). The
/// value of the model is the one SQL returns, padded with spaces to the width of the column;
/// a target whose framework trims it (<see cref="Descriptors.TargetFrameworkDescriptor.TrimsFixedWidthOnRead"/>)
/// gets the projection as a concatenation with empty text, which reaches the framework as
/// variable-width text with the padding in it, and a target that does not trim gets the
/// value of a property the source's framework trimmed (<see cref="PropertyMap.TrimmedOnRead"/>)
/// trimmed of its trailing spaces. Only what the program receives is rewritten - the
/// outermost select list and that of each member of a set operation at the top -, never a
/// subquery, a definition, a condition or a key: the database evaluates those over the
/// padded values in every framework, and its comparisons pass over trailing spaces.
/// </summary>
public abstract partial class AbstractQueryBuilder
{
    /// <summary>The empty text a fixed-width value is concatenated with.</summary>
    private static readonly QueryOperand EmptyText = QueryOperand.Value(QueryConstant.Of(string.Empty, ScalarType.String));

    /// <summary>
    /// The rewritten projections of an aggregate the source left without an alias: they name
    /// nothing, exactly as the aggregate they wrap, and the gate of the nameless expression
    /// (decision 107) lets them through.
    /// </summary>
    private readonly HashSet<ProjectInstruction> namelessReads = [];

    /// <summary>The query with the projections of fixed-width values rewritten as the source read them.</summary>
    private IReadOnlyList<QueryInstruction> ReadFixedWidthAsTheSource(IReadOnlyList<QueryInstruction> body)
    {
        namelessReads.Clear();
        return FixedWidthScope(body);
    }

    private IReadOnlyList<QueryInstruction> FixedWidthScope(IReadOnlyList<QueryInstruction> body)
    {
        var scope = Unwrap(body);
        if (scope.Count > 0 && scope[0] is SetOperationInstruction operation)
        {
            var left = FixedWidthScope(operation.Left.Instructions);
            var right = FixedWidthScope(operation.Right.Instructions);
            if (ReferenceEquals(left, operation.Left.Instructions) && ReferenceEquals(right, operation.Right.Instructions))
            {
                return body;
            }

            return
            [
                operation with { Left = new SubQueryInstruction([.. left]), Right = new SubQueryInstruction([.. right]) },
                .. scope.Skip(1),
            ];
        }

        if (!scope.OfType<ProjectInstruction>().Any())
        {
            return body;
        }

        var aliases = ScopeAliases(scope, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), RenderedEntityFor);
        var distinct = scope.OfType<DistinctInstruction>().Any();
        var orderings = scope.OfType<OrderByInstruction>().ToList();
        var rewritten = new List<string>();
        var result = new List<QueryInstruction>(scope.Count);

        foreach (var instruction in scope)
        {
            if (instruction is not ProjectInstruction projection || Reread(projection, aliases) is not { } read)
            {
                result.Add(instruction);
                continue;
            }

            // Under DISTINCT every key of the ordering is a projected column (decision 073),
            // and SQL Server pairs a key with the select list by its text: the rewritten value
            // is no longer the column, and EclipseLink binds the empty text as a parameter of
            // its own in each place, so the projection stays as the source wrote it.
            if (distinct && orderings.Any(order => Projects(projection, order)))
            {
                Report(
                    ConversionRecordKind.Loss,
                    $"The projection '{projection.Operand}' reads a fixed-width column under DISTINCT and is also a key of the ordering, so it cannot be rewritten; "
                    + (Descriptor.TrimsFixedWidthOnRead
                        ? $"{Descriptor.Framework} hands its value over without the trailing spaces SQL pads it with (decision 169)."
                        : $"{Descriptor.Framework} hands its value over padded with spaces, where the source's framework trimmed them (decision 169)."),
                    QueryFeature.Projection);
                result.Add(instruction);
                continue;
            }

            result.Add(read);
            rewritten.Add(projection.Operand.ToString());
        }

        if (rewritten.Count == 0)
        {
            return body;
        }

        {
            var named = string.Join(", ", rewritten.Select(r => $"'{r}'"));
            Report(
                ConversionRecordKind.Convention,
                Descriptor.TrimsFixedWidthOnRead
                    ? $"{Descriptor.Framework} hands a fixed-width value over without the trailing spaces SQL pads it with; the projection of {named} "
                      + "is written as a concatenation with empty text, which reaches it as variable-width text and keeps the padding (decision 169)."
                    : $"The source's framework handed the fixed-width value of {named} to its program without trailing spaces, and {Descriptor.Framework} "
                      + "hands it over padded; the projection is written trimmed of trailing spaces (decision 169).",
                QueryFeature.Projection);
        }

        return result;
    }

    /// <summary>The projection rewritten so that the target hands over what the source did, or null where nothing needs it.</summary>
    private ProjectInstruction? Reread(ProjectInstruction projection, Dictionary<string, EntityMap> aliases)
    {
        var operand = projection.Operand;

        QueryOperand read;
        if (Descriptor.TrimsFixedWidthOnRead)
        {
            if (!PaddedInTheSource(operand, aliases))
            {
                return null;
            }

            read = QueryOperand.Computed(QueryExpression.Binary(ExpressionOperator.Concat, operand, EmptyText));
        }
        else
        {
            if (!TrimmedInTheSource(operand, aliases))
            {
                return null;
            }

            read = QueryOperand.Computed(QueryExpression.Call(QueryFunction.TrimEnd, [operand]));
        }

        if (projection.Alias is not null)
        {
            return projection with { Operand = read };
        }

        // A bare column keeps the name it came back under: the column in SQL, the property in
        // a language that names it as a member. That is the name the projection had, not one
        // the tool invents (decision 028).
        if (operand is { IsAggregate: false } && FixedWidthColumn(operand, aliases) is { } column)
        {
            var name = WritesColumnsAsMembers ? column.Property.Name : operand.Property!;
            return projection with { Operand = read, Alias = name, DelimitedAlias = !WritesColumnsAsMembers && !Identifiers.IsRegular(name) };
        }

        var nameless = projection with { Operand = read };
        namelessReads.Add(nameless);
        return nameless;
    }

    /// <summary>
    /// Whether the value is fixed-width text a trimming target would cut and the source's
    /// framework did not: a column, a MIN or MAX over one, a COALESCE or a CASE one of whose
    /// values is such - each measured to come back trimmed from EclipseLink. Over-reaching is
    /// harmless here: a concatenation with empty text is the same value whatever its type.
    /// </summary>
    private bool PaddedInTheSource(QueryOperand operand, Dictionary<string, EntityMap> aliases)
    {
        if (operand.IsColumn && (!operand.IsAggregate || IsMinOrMax(operand)))
        {
            return FixedWidthColumn(operand, aliases) is { TrimmedOnRead: false };
        }

        if (operand is not { IsExpression: true, IsAggregate: false })
        {
            return false;
        }

        var expression = operand.Expression!;
        if (expression is { IsCall: true, Function: QueryFunction.Coalesce })
        {
            return expression.Arguments!.Any(argument => PaddedInTheSource(argument, aliases));
        }

        if (expression.IsCase)
        {
            return expression.Branches!.Any(branch => PaddedInTheSource(branch.Then, aliases))
                   || expression.Else is { } otherwise && PaddedInTheSource(otherwise, aliases);
        }

        return false;
    }

    /// <summary>
    /// Whether the value is a fixed-width column, or a MIN or MAX over one, whose values the
    /// source's framework trimmed. Narrower than its counterpart on purpose: trimming a value
    /// the source's framework handed over padded would change it.
    /// </summary>
    private bool TrimmedInTheSource(QueryOperand operand, Dictionary<string, EntityMap> aliases)
        => operand.IsColumn && (!operand.IsAggregate || IsMinOrMax(operand))
           && FixedWidthColumn(operand, aliases) is { TrimmedOnRead: true };

    private static bool IsMinOrMax(QueryOperand operand)
        => string.Equals(operand.Function, "MIN", StringComparison.OrdinalIgnoreCase)
           || string.Equals(operand.Function, "MAX", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mapping of a column whose type is fixed-width text, through the row its alias stands for; null for anything else.</summary>
    private static PropertyMap? FixedWidthColumn(QueryOperand operand, Dictionary<string, EntityMap> aliases)
        => operand is { IsColumn: true, Table: { } alias, Property: not "*" }
           && aliases.TryGetValue(alias, out var map)
           && ColumnMember.TypedBy(map, operand.Property!) is { Type: DatabaseType.Char } column
            ? column
            : null;
}
