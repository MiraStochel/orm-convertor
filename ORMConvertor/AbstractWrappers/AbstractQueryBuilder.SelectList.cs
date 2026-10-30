using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The rules of the select list and of the columns a query names that hold for every target
/// (decision 023), kept beside the template: which row a bare column belongs to, which row a
/// whole-row projection materializes, and what a target whose language names columns as
/// members of its own can and cannot write.
/// </summary>
public abstract partial class AbstractQueryBuilder
{
    /// <summary>
    /// Whether the target's query language names a column as a member of the row it belongs
    /// to - a property of an entity under an alias, as LINQ, HQL and JPQL do - rather than as
    /// a name of the result set, as SQL does. Such a target cannot write a column whose row
    /// nobody knows, nor an alias that is no identifier of its language, and writes the query
    /// in native SQL instead (decision 113); a SQL target writes the first bare, as the source
    /// did, and delimits the second in its own spelling.
    /// </summary>
    protected virtual bool WritesColumnsAsMembers => false;

    /// <summary>
    /// Gives every bare column of the query the row it belongs to, as far as the mapping
    /// representation says it. A scope over one row source owns every bare column it names,
    /// which is what the source meant by leaving it bare. A scope over a join leaves the
    /// question to the schema: the one row whose entity maps the column - a scalar property
    /// or a foreign key a reference holds - owns it, and where no entity of the conversion
    /// maps it, or the table behind an alias is no entity of the conversion at all, the column
    /// stays bare. Qualifying it with the first table of the join, which the readers used to
    /// do, wrote a different column, or one the database refuses. An ordering key that names
    /// the alias of a projection is that alias and no column. Walks every scope the way the
    /// typing of temporal literals does; the maps a scope resolves through are those of its
    /// own aliases, the enclosing ones beneath them.
    /// </summary>
    private IReadOnlyList<QueryInstruction> QualifyColumns(IReadOnlyList<QueryInstruction> body, Dictionary<string, EntityMap> enclosing)
    {
        var aliases = ScopeAliases(body, enclosing, RenderedEntityFor);
        var declared = body.OfType<FromInstruction>().Select(f => f.Alias ?? f.Table)
            .Concat(body.OfType<JoinInstruction>().Select(j => j.RightTableAlias ?? j.RightTable))
            .ToList();
        var projectionAliases = body.OfType<ProjectInstruction>().Select(p => p.Alias).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The owner of a bare column in this scope, or null to leave it bare.
        string? OwnerOf(string column)
        {
            if (declared.Count == 1)
            {
                return declared[0];
            }

            var owners = declared
                .Where(alias => aliases.TryGetValue(alias, out var map) && MapsColumn(map, column))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return owners.Count == 1 ? owners[0] : null;
        }

        QueryOperand Operand(QueryOperand operand)
        {
            if (operand.IsSubQuery)
            {
                return QueryOperand.Nested(operand.SubQuery! with { Instructions = [.. QualifyColumns(operand.SubQuery.Instructions, aliases)] });
            }

            if (operand.IsExpression)
            {
                var rebuilt = operand.Expression!.Rebuilt(Operand, Condition);
                return rebuilt is null || ReferenceEquals(rebuilt, operand.Expression)
                    ? operand
                    : QueryOperand.Computed(rebuilt, operand.Function, operand.Distinct);
            }

            if (operand is { IsColumn: true, Table: null, Property: not "*" } && OwnerOf(operand.Property!) is { } owner)
            {
                return QueryOperand.Column(owner, operand.Property!, operand.Function, operand.Distinct);
            }

            return operand;
        }

        ConditionNode Condition(ConditionNode node) => node switch
        {
            ComparisonCondition comparison => comparison with
            {
                Left = Operand(comparison.Left),
                Right = comparison.Right is null ? null : Operand(comparison.Right),
            },
            LogicalCondition logical => logical with { Operands = [.. logical.Operands.Select(Condition)] },
            NotCondition negation => negation with { Operand = Condition(negation.Operand) },
            _ => node,
        };

        var result = new List<QueryInstruction>(body.Count);
        foreach (var instruction in body)
        {
            result.Add(instruction switch
            {
                ProjectInstruction projection => projection with { Operand = Operand(projection.Operand) },
                // An ordering by the alias of a projection names the projection (decision 073).
                OrderByInstruction { Operand: { IsColumn: true, Table: null, IsAggregate: false } key } order
                    when projectionAliases.Contains(key.Property!) => order,
                OrderByInstruction order => order with { Operand = Operand(order.Operand) },
                GroupByInstruction grouping => grouping with { Key = Operand(grouping.Key) },
                SelectInstruction filter => filter with { Condition = Condition(filter.Condition) },
                HavingInstruction postFilter => postFilter with { Condition = Condition(postFilter.Condition) },
                JoinInstruction { OnCondition: null } cross => cross,
                JoinInstruction join => join with { OnCondition = Condition(join.OnCondition!) },
                SubQueryInstruction nested => nested with { Instructions = [.. QualifyColumns(nested.Instructions, aliases)] },
                SetOperationInstruction operation => operation with
                {
                    Left = operation.Left with { Instructions = [.. QualifyColumns(operation.Left.Instructions, enclosing)] },
                    Right = operation.Right with { Instructions = [.. QualifyColumns(operation.Right.Instructions, enclosing)] },
                },
                _ => instruction,
            });
        }

        return result;
    }

    /// <summary>Whether the entity maps the column: a scalar property maps it, or an owning reference holds it as a part of its foreign key.</summary>
    private static bool MapsColumn(EntityMap map, string column)
        => ColumnMember.ScalarOf(map, column) is not null || ColumnMember.HeldBy(map, column) is not null;

    /// <summary>
    /// The rules of one scope's select list, held once for every target before the clauses
    /// are composed. Returns the projections to compose with, or null when the scope cannot
    /// be written in the target's own language, the reason on the channel.
    /// <list type="bullet">
    /// <item>The whole row of the only range variable is the whole row of the query, which
    /// rule Q3 spells as the absence of a projection: <c>select c from Customer c</c>,
    /// <c>SELECT c.*</c> and <c>SELECT *</c> over one table are one query.</item>
    /// <item>A whole-row projection beside other projections is a shape the representation
    /// does not carry - a row and columns in one result -, so the row is dropped with a
    /// record and the columns stand, which is what every reader did on its own before.</item>
    /// <item>A column still bare in a scope over a join has a row nobody knows; a target that
    /// names a column as a member of its row cannot write it in its own language and writes
    /// the query in native SQL, where the column stands bare as the source wrote it
    /// (decision 113: the mapping the catalog supplies decides the direction). The SQL
    /// targets write it bare.</item>
    /// <item>An alias the source delimited because it is no identifier - <c>"product.productId"</c>
    /// - has no member name in such a target either and goes the same way; one that is an
    /// identifier after all (<c>[Name]</c>) was delimited for nothing the target needs and is
    /// written bare.</item>
    /// </list>
    /// </summary>
    private IReadOnlyList<ProjectInstruction>? GateSelectList(IReadOnlyList<QueryInstruction> body, List<ProjectInstruction> projections)
    {
        var sources = body.OfType<FromInstruction>().Count() + body.OfType<JoinInstruction>().Count();

        if (sources == 1 && projections is [{ IsWholeRow: true }])
        {
            return [];
        }

        if (projections.Count > 1 && projections.Any(p => p.IsWholeRow))
        {
            foreach (var whole in projections.Where(p => p.IsWholeRow))
            {
                Report(
                    ConversionRecordKind.Loss,
                    $"The whole row of '{whole.Operand.Table}' is projected beside other columns, which is not a shape the query representation carries; the row was dropped and the columns are projected.",
                    QueryFeature.Projection);
            }

            projections = projections.Where(p => !p.IsWholeRow).ToList();
        }

        if (!WritesColumnsAsMembers)
        {
            return projections;
        }

        if (sources > 1)
        {
            var projectionAliases = projections.Select(p => p.Alias).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var instruction in body)
            {
                if (instruction is OrderByInstruction { Operand: { IsColumn: true, Table: null, IsAggregate: false } key } && projectionAliases.Contains(key.Property!))
                {
                    continue;
                }

                if (OperandsOf(instruction).FirstOrDefault(o => o is { IsColumn: true, Table: null, Property: not "*" }) is not { } bare)
                {
                    continue;
                }

                ReportUnspoken(
                    $"The column '{bare.Property}' is written without its table in a query over {sources} tables, and no mapping of the conversion says which of them holds it, so {FormName} cannot name it as a member of its row; a catalog of the database supplies the mapping",
                    FeatureOf(instruction));
                return null;
            }
        }

        var delimited = projections.FirstOrDefault(p => p is { DelimitedAlias: true, Alias: { } alias } && !Identifiers.IsRegular(alias));
        if (delimited is not null)
        {
            ReportUnspoken(
                $"The alias '{delimited.Alias}' is no identifier - the source had to delimit it - and {FormName} has no member name for it",
                QueryFeature.Projection);
            return null;
        }

        return projections;
    }

    /// <summary>The feature a record about an instruction names: the clause the instruction is.</summary>
    private static QueryFeature FeatureOf(QueryInstruction instruction) => instruction switch
    {
        JoinInstruction => QueryFeature.Join,
        SelectInstruction => QueryFeature.Filtering,
        GroupByInstruction or HavingInstruction => QueryFeature.Grouping,
        OrderByInstruction => QueryFeature.Ordering,
        _ => QueryFeature.Projection,
    };
}
