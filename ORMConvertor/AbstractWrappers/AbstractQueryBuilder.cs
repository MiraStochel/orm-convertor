using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace AbstractWrappers;

/// <summary>
/// Records what a parser reads out of a source query and turns it into the target's own
/// query form.
///
/// Filling is the fluent half — From, Project, Where and the rest — and is implemented
/// here, because it is a property of the query IR rather than of any framework. Generation
/// is a template method (decision 023): <see cref="Build"/> normalizes the recorded
/// instructions into <see cref="QueryClauses"/>, reports what the target cannot express,
/// then runs seven abstract steps in relational evaluation order and lets the framework
/// assemble the text in <see cref="FinalizeQuery"/>.
/// </summary>
public abstract class AbstractQueryBuilder
{
    protected readonly List<QueryInstruction> instructions = [];
    protected readonly Stack<int> marks = [];
    private SetOperationInstruction? initiatedSetOperation = null;

    /// <summary>
    /// Declaration of what the target framework can express, mapping facts and query
    /// features alike (decisions 009 and 022).
    /// </summary>
    public abstract TargetFrameworkDescriptor Descriptor { get; }

    /// <summary>
    /// Mapping IR of the same conversion, handed over by the orchestration before
    /// <see cref="Build"/>. A target whose query language names entities and properties
    /// rather than tables and columns — LINQ, HQL, JPQL — has to map back through it, which
    /// is the inverse of what a query parser does on the way in.
    /// </summary>
    public IReadOnlyList<EntityMap> EntityMaps { get; set; } = [];

    /// <summary>
    /// Associates each table alias used by the query with the entity it stands for, so that
    /// a condition naming a column can be rendered as a property.
    /// </summary>
    protected Dictionary<string, EntityMap> AliasedEntities(QueryClauses clauses)
    {
        var byAlias = new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase);

        void Add(string? alias, string table)
        {
            var map = EntityFor(table);
            if (map is not null && alias is not null)
            {
                byAlias[alias] = map;
            }
        }

        Add(clauses.From.Alias ?? clauses.From.Table, clauses.From.Table);
        foreach (var join in clauses.Joins)
        {
            Add(join.RightTableAlias ?? join.RightTable, join.RightTable);
        }

        return byAlias;
    }

    /// <summary>
    /// The entity mapped to a table, matched on the qualified name first and on the bare
    /// table name after it.
    /// </summary>
    protected EntityMap? EntityFor(string table)
    {
        var bare = table.Split('.').LastOrDefault() ?? table;

        return EntityMaps.FirstOrDefault(m =>
                   string.Equals($"{m.Schema}.{m.Table}", table, StringComparison.OrdinalIgnoreCase))
               ?? EntityMaps.FirstOrDefault(m =>
                   string.Equals(m.Table, bare, StringComparison.OrdinalIgnoreCase))
               ?? EntityMaps.FirstOrDefault(m =>
                   string.Equals(m.Entity.Name, bare, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The property a column belongs to. Falls back to the column name itself, which is the
    /// right answer whenever the framework's own convention would have produced it anyway.
    /// </summary>
    protected static string PropertyFor(EntityMap? map, string column)
        => map?.PropertyMaps.FirstOrDefault(p =>
               string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
               ?.Property.Name
           ?? column;

    private readonly List<ConversionRecord> records = [];

    private bool refused;

    /// <summary>
    /// Diagnostic records of this query's translation (decisions 010 and 022). A query
    /// builder is created per query, so the orchestration concatenates these with the
    /// entity builder's before returning them.
    /// </summary>
    public IReadOnlyList<ConversionRecord> Records => records;

    /// <summary>
    /// Adds a record. Public for the same reason it is on the entity builder: a loss can
    /// occur on the way into the model, so a parser reports here too.
    ///
    /// A <see cref="ConversionRecordKind.Failure"/> means the artifact does not come out -
    /// the sentence the entity side has had since decision 010, now said here too
    /// (decision 053). Held by the channel rather than by each builder remembering to
    /// return early, so a query that cannot be rendered faithfully cannot leave a
    /// half-rendered one behind.
    /// </summary>
    public void Report(ConversionRecord record)
    {
        records.Add(record);

        if (record.Kind == ConversionRecordKind.Failure)
        {
            refused = true;
        }
    }

    protected void Report(
        ConversionRecordKind kind,
        string reason,
        QueryFeature? feature = null,
        string? entity = null,
        string? property = null)
        => Report(new ConversionRecord
        {
            Kind = kind,
            Framework = Descriptor.Framework,
            Artifact = ConversionContentType.CSharpQuery,
            Entity = entity,
            Property = property,
            Feature = feature,
            Reason = reason,
        });

    public void Push()
    {
        marks.Push(instructions.Count);
    }

    public void Pop()
    {
        var start = marks.Pop();
        var body = instructions.GetRange(start, instructions.Count - start);
        instructions.RemoveRange(start, instructions.Count - start);

        // Store instruction into a subquery, unless there is an ongoing set operation.
        if (initiatedSetOperation != null) // TODO does not keep track of level of nesting
        {
            var newSetOp = new SetOperationInstruction(
                initiatedSetOperation.OperationType,
                initiatedSetOperation.Left,
                new SubQueryInstruction(body)
            );
            instructions.Add(newSetOp);
            initiatedSetOperation = null;
        }
        else
        {
            instructions.Add(new SubQueryInstruction(body));
        }
    }

    public void From(string table, string? alias = null)
    {
        instructions.Add(new FromInstruction(table, alias));
    }

    public void Project(string table, string attr, string? alias = null, string? function = null)
    {
        instructions.Add(new ProjectInstruction(table, attr, alias, function));
    }

    public void Where(ConditionNode condition)
    {
        instructions.Add(new SelectInstruction(condition));
    }

    public void Join(JoinKind kind, string left, string right, ConditionNode onCondition, string? rightTableAlias = null)
    {
        instructions.Add(new JoinInstruction(kind, left, right, rightTableAlias, onCondition));
    }

    public void GroupBy(string table, string attr)
    {
        instructions.Add(new GroupByInstruction(table, attr));
    }

    public void OrderBy(string? table, string attributeOrAlias, bool asc = true)
    {
        instructions.Add(new OrderByInstruction(table, attributeOrAlias, asc));
    }

    public void Having(ConditionNode condition)
    {
        instructions.Add(new HavingInstruction(condition));
    }

    public void SetOperation(SetOperationType operation)
    {
        // Guarded before Last(), which on an empty list throws "Sequence contains no
        // elements" - the commonest misuse would otherwise never see the message below.
        if (instructions.Count == 0 || instructions[^1] is not SubQueryInstruction subQuery)
        {
            throw new InvalidOperationException("Set operation can only be initiated after a subquery has been defined. Use Push() to start a subquery and Pop() to end it.");
        }
        instructions.RemoveAt(instructions.Count - 1);

        initiatedSetOperation = new SetOperationInstruction(operation, subQuery, new SubQueryInstruction([]));
    }

    /// <summary>
    /// Generates the target artifacts. Concrete here so that normalization, capability
    /// reporting and step order cannot drift between frameworks (decision 023). Returns an
    /// empty list when the query could not be built; the reason is always in
    /// <see cref="Records"/> rather than in an exception (decision 010).
    ///
    /// A failure reported anywhere along the way - by the parser, by the gate below or by a
    /// visitor at the point of emission - discards the artifact even if the text has
    /// meanwhile been assembled (decision 053).
    /// </summary>
    public List<ConversionSource> Build()
    {
        var artifacts = BuildArtifacts();

        return refused ? [] : artifacts;
    }

    private List<ConversionSource> BuildArtifacts()
    {
        if (instructions.Count == 0)
        {
            Report(ConversionRecordKind.Failure, "The query carries no instructions.");
            return [];
        }

        if (instructions[0] is SetOperationInstruction setOperation)
        {
            return BuildSetOperation(setOperation);
        }

        var body = instructions.Count == 1 && instructions[0] is SubQueryInstruction top
            ? top.Instructions
            : instructions;

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return [];
        }

        var artifact = Compose(clauses);
        return FinalizeQuery(clauses, artifact);
    }

    /// <summary>
    /// Runs the seven steps over one set of clauses. Separate from <see cref="Build"/> so
    /// that a builder overriding <see cref="BuildSetOperation"/> can render each operand
    /// through the same steps.
    /// </summary>
    protected QueryArtifact Compose(QueryClauses clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        ReportCapabilityLosses(clauses);

        var artifact = new QueryArtifact();

        // Relational evaluation order, which is what a LINQ chain writes literally and what
        // SQL and HQL permute only on the surface. It is a data dependency, not a style: a
        // LINQ projection lambda binds a grouping after GroupBy and an element before it, so
        // the projection cannot be composed until grouping is known.
        BuildSource(clauses, artifact);
        BuildJoins(clauses, artifact);
        BuildFilter(clauses, artifact);
        BuildGrouping(clauses, artifact);
        BuildPostFilter(clauses, artifact);
        BuildOrdering(clauses, artifact);
        BuildProjection(clauses, artifact);

        return artifact;
    }

    /// <summary>
    /// Sorts the recorded instructions into clauses and applies the rules that hold for
    /// every target. Returns null when the query cannot be built at all, having reported
    /// why.
    /// </summary>
    protected QueryClauses? Normalize(IReadOnlyList<QueryInstruction> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var sources = body.OfType<FromInstruction>().ToList();

        // Rule Q2: each (sub)query defines exactly one logical source.
        if (sources.Count == 0)
        {
            Report(ConversionRecordKind.Failure, "The query names no source table (rule Q2).");
            return null;
        }

        if (sources.Count > 1)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The query names {sources.Count} source tables; exactly one is allowed (rule Q2).");
            return null;
        }

        var nested = body.OfType<SubQueryInstruction>().ToList();
        if (nested.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                "A nested subquery was read but is not rendered; the output is poorer than the input.",
                QueryFeature.Subquery);
        }

        // A malformed condition cannot be rendered without changing which rows the query
        // returns, so it is refused here rather than approximated by each target in its own
        // way (decision 053). One gate for all three, like the step order of decision 023.
        var conditions = body.OfType<SelectInstruction>().Select(i => i.Condition)
            .Concat(body.OfType<HavingInstruction>().Select(i => i.Condition))
            .Concat(body.OfType<JoinInstruction>().Select(j => j.OnCondition));

        if (conditions.Any(condition => !ConditionIsWellFormed(condition)))
        {
            return null;
        }

        var projections = body.OfType<ProjectInstruction>().ToList();
        var groupBys = body.OfType<GroupByInstruction>().ToList();

        // Rule Q8: grouping is mandatory when aggregates sit next to plain columns. A query
        // that is nothing but aggregates needs no grouping, so that case is not reported.
        if (groupBys.Count == 0
            && projections.Any(p => p.Function is not null)
            && projections.Any(p => p.Function is null))
        {
            Report(
                ConversionRecordKind.Incompleteness,
                "Aggregated and plain columns are projected together without a grouping (rule Q8).",
                QueryFeature.Grouping);
        }

        return new QueryClauses
        {
            From = sources[0],
            Projections = projections,
            Joins = [.. body.OfType<JoinInstruction>()],
            Filter = Conjoin([.. body.OfType<SelectInstruction>().Select(i => i.Condition)]),
            GroupBys = groupBys,
            PostFilter = Conjoin([.. body.OfType<HavingInstruction>().Select(i => i.Condition)]),
            OrderBys = [.. body.OfType<OrderByInstruction>()],
        };
    }

    /// <summary>
    /// Whether a condition tree can be rendered at all (decision 053). Two shapes cannot:
    /// a comparison whose operator needs a right operand and has none, and a logical node
    /// with no operands. Both used to be answered by each visitor on its own - one threw,
    /// two substituted a tautology - and a tautology in place of a filter returns every row
    /// the source excluded, and inside a disjunction invalidates the whole condition. The
    /// null tests are the exception the model itself defines: their right operand is
    /// deliberately unused (decision 002).
    /// </summary>
    private bool ConditionIsWellFormed(ConditionNode node)
    {
        switch (node)
        {
            case ComparisonCondition comparison:
                if (comparison.Operator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
                {
                    return true;
                }

                if (comparison.Right is null)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"A comparison with operator {comparison.Operator} carries no right operand, so the condition cannot be rendered without changing which rows the query returns; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                return true;

            case LogicalCondition logical:
                if (logical.Operands.Count == 0)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"A logical {logical.Operator} carries no operand, so the condition cannot be rendered; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                return logical.Operands.All(ConditionIsWellFormed);

            case NotCondition negation:
                return ConditionIsWellFormed(negation.Operand);

            default:
                return true;
        }
    }

    /// <summary>
    /// Rule Q4: several filters are combined by conjunction. One implementation for both
    /// WHERE and HAVING and for every target.
    /// </summary>
    private static ConditionNode? Conjoin(IReadOnlyList<ConditionNode> conditions) => conditions.Count switch
    {
        0 => null,
        1 => conditions[0],
        _ => new LogicalCondition(LogicalOperator.And, conditions),
    };

    /// <summary>
    /// Reports the query features the model carries and the descriptor marks inexpressible
    /// (rule Q14). Mechanical on purpose — a builder that had to remember to report would
    /// eventually not (decision 009).
    /// </summary>
    private void ReportCapabilityLosses(QueryClauses clauses)
    {
        void Check(QueryFeature feature, bool present)
        {
            if (present && Descriptor.SupportOf(feature) == FactSupport.NotExpressible)
            {
                Report(
                    ConversionRecordKind.Loss,
                    $"The target cannot express {feature}; the artifact is generated without it.",
                    feature);
            }
        }

        Check(QueryFeature.Projection, clauses.Projections.Count > 0);
        Check(QueryFeature.Filtering, clauses.Filter is not null);
        Check(QueryFeature.Join, clauses.Joins.Count > 0);
        Check(QueryFeature.Aggregation, clauses.HasAggregates);
        Check(QueryFeature.Grouping, clauses.GroupBys.Count > 0);
        Check(QueryFeature.PostAggregationFiltering, clauses.PostFilter is not null);
        Check(QueryFeature.Ordering, clauses.OrderBys.Count > 0);
    }

    /// <summary>
    /// Renders a set operation. The default refuses with a record, because a target whose
    /// query language has no UNION cannot be given one; a target that has one overrides.
    /// </summary>
    protected virtual List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        Report(
            ConversionRecordKind.Loss,
            "The target cannot express a set operation; no query artifact was generated.",
            QueryFeature.SetOperation);
        return [];
    }

    protected abstract void BuildSource(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildJoins(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildFilter(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildGrouping(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildOrdering(QueryClauses clauses, QueryArtifact artifact);

    protected abstract void BuildProjection(QueryClauses clauses, QueryArtifact artifact);

    /// <summary>
    /// Joins the slots into the artifacts of the target framework. The count of artifacts is
    /// a property of the framework, which is why this returns a list (decision 025).
    /// </summary>
    protected abstract List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact);
}
