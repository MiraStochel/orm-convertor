using System.Globalization;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Convertors;
using Common.Naming;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
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
/// then runs eight abstract steps in relational evaluation order and lets the framework
/// assemble the text in <see cref="FinalizeQuery"/>.
/// </summary>
public abstract class AbstractQueryBuilder
{
    protected readonly List<QueryInstruction> instructions = [];
    protected readonly Stack<int> marks = [];

    /// <summary>
    /// Set operations armed by <see cref="SetOperation"/> and still waiting for their right
    /// operand, each remembering how deep the mark stack stood when it was armed. Only the
    /// Pop that returns to that depth completes the operation - a scope opened and closed
    /// inside the right operand must not: a single flag used to complete the operation on
    /// whichever Pop came first, which mis-assembled any nested right side.
    /// </summary>
    private readonly Stack<(SetOperationType Operation, SubQueryInstruction Left, int Depth)> pendingSetOperations = [];

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
            // A source that states no table - Dapper, MyBatis - still names its class the
            // way the one naming convention derives it from the table (decision 050), and
            // the alias resolves to that class, so that a column renders as the property
            // the class really declares and typed as it declares it. The scalar gate keeps
            // its own resolution, so what a parameter's scalar follows from is unchanged.
            var map = RenderedEntityFor(table);
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
    /// table name after it. A name the query defines as an intermediate result answers with
    /// the row the template describes for it (decision 112), typed through stated mappings
    /// only, as everything this method answers is.
    /// </summary>
    protected EntityMap? EntityFor(string table)
    {
        if (statedRows.TryGetValue(table, out var row))
        {
            return row;
        }

        var bare = table.Split('.').LastOrDefault() ?? table;

        return EntityMaps.FirstOrDefault(m =>
                   string.Equals($"{m.Schema}.{m.Table}", table, StringComparison.OrdinalIgnoreCase))
               ?? EntityMaps.FirstOrDefault(m =>
                   string.Equals(m.Table, bare, StringComparison.OrdinalIgnoreCase))
               ?? EntityMaps.FirstOrDefault(m =>
                   string.Equals(m.Entity.Name, bare, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The entity a table renders as: the row of an intermediate result of that name typed
    /// the way rendering types (decision 112), the stated mapping, or the class the naming
    /// convention of decision 050 derives. The resolution of every walk that types what the
    /// visitors write; the parameter gate keeps <see cref="EntityFor"/>.
    /// </summary>
    private EntityMap? RenderedEntityFor(string table)
        => renderedRows.TryGetValue(table, out var row) ? row : EntityFor(table) ?? ByDerivedName(table);

    /// <summary>The entity whose name the naming convention derives from the table (decision 050), where no mapping names the table.</summary>
    private EntityMap? ByDerivedName(string table)
    {
        var derived = EntityTableNaming.EntityNameFor(table.Split('.').LastOrDefault() ?? table);

        return EntityMaps.FirstOrDefault(m => string.Equals(m.Entity.Name, derived, StringComparison.OrdinalIgnoreCase));
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
    /// The constructs of the query the target's query language does not speak, as the
    /// builder met them in this Build (decision 113): each one a record of kind
    /// <see cref="ConversionRecordKind.Fallback"/> whose reason names the construct and the
    /// language, and not yet a record of the conversion - what it becomes is decided once
    /// the attempt is over, by <see cref="FallBack"/>.
    /// </summary>
    private readonly List<ConversionRecord> unspoken = [];

    /// <summary>
    /// Set on the builder the template falls back to (decision 113): it writes the native
    /// SQL of the target's dialect, which speaks every construct of the vocabulary - that is
    /// the measure decision 113 gives the vocabulary -, so the declaration of what the
    /// target's query language speaks does not apply to it, while the descriptor still
    /// names the framework the records belong to.
    /// </summary>
    private bool writesNativeSql;

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
    ///
    /// A <see cref="ConversionRecordKind.Fallback"/> is the other half of the channel
    /// (decision 113): a step or a visitor met a shape its query language does not have,
    /// and says so the way it would refuse - one channel for the declaration and the point
    /// of emission alike. It is held back rather than recorded, because whether it becomes
    /// the native SQL of the dialect or a refusal is the template's to decide.
    /// </summary>
    public void Report(ConversionRecord record)
    {
        if (record.Kind == ConversionRecordKind.Fallback)
        {
            unspoken.Add(record);
            return;
        }

        records.Add(record);

        if (record.Kind == ConversionRecordKind.Failure)
        {
            refused = true;
        }
    }

    /// <summary>
    /// The language of the runnable method the builder emits, which the records of the
    /// template name as their artifact: C# for the .NET targets, Java for the JPA ones
    /// (decision 077).
    /// </summary>
    protected virtual ConversionContentType MethodArtifact => ConversionContentType.CSharpQuery;

    /// <summary>
    /// The name the source gave this query, verbatim (decision 081): the name attribute of
    /// an hbm.xml &lt;query&gt;, of a @NamedQuery, the id of a MyBatis &lt;select&gt;. A bare SQL
    /// unit names nothing, and neither does a unit of code - a Dapper call, a createQuery, a
    /// LINQ chain -, so when such a unit carries several queries, each gets its position in
    /// the text - Query01, Query02 (decisions 108 and 109, <c>QueryMethodNaming.Positional</c>) -
    /// because that is the only name under which the user finds it among the output. Null
    /// for a query that shares its unit with no other, which needs no name to be told from a
    /// neighbour. Set by the parser that read the query; the orchestration copies it into
    /// the records of this builder, so three failed queries of one document stop being three
    /// records distinguishable only by their order.
    /// </summary>
    public string? QueryName { get; set; }

    /// <summary>
    /// The name of the generated method: the source's name spelled as an identifier of the
    /// target language, the fixed fallback where the source named nothing. Overridden by a
    /// target whose methods are not PascalCase.
    /// </summary>
    protected virtual string MethodName => QueryMethodNaming.PascalCase(QueryName, "Query");

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
            Artifact = MethodArtifact,
            Entity = entity,
            Property = property,
            Feature = feature,
            Reason = reason,
        });

    /// <summary>
    /// Says that the target's query language does not speak a construct of the query
    /// (decision 113) - the report a step makes where it used to refuse because the shape is
    /// not in its language, as opposed to a shape no target writes. The reason names the
    /// construct and the language, without the consequence: the template adds that once it
    /// knows whether the target falls back to native SQL or refuses.
    /// </summary>
    protected void ReportUnspoken(string reason, QueryFeature feature)
        => Report(ConversionRecordKind.Fallback, reason, feature);

    /// <summary>
    /// A builder that writes this target's query whole in the native SQL of its dialect and
    /// wraps it into the framework's API for native queries, which the descriptor names as
    /// <see cref="TargetFrameworkDescriptor.NativeSqlApi"/> (decision 113). Null for a target
    /// without one. A fresh builder each call; the template hands it the query.
    /// </summary>
    protected virtual AbstractQueryBuilder? NativeSqlBuilder() => null;

    public void Push()
    {
        marks.Push(instructions.Count);
    }

    public void Pop()
    {
        var closed = CloseScope();

        // The closed scope is the right operand of an armed set operation only when this Pop
        // returns to the depth the operation was armed at; deeper scopes belong to the
        // operand's own inner structure.
        if (pendingSetOperations.Count > 0 && pendingSetOperations.Peek().Depth == marks.Count)
        {
            var (operation, left, _) = pendingSetOperations.Pop();
            instructions.Add(new SetOperationInstruction(operation, left, closed));
            return;
        }

        instructions.Add(closed);
    }

    /// <summary>
    /// Closes the current scope and hands it back instead of appending it, which is how a
    /// parser reads a subquery destined for an operand position (decision 061): the nested
    /// instructions become the operand's own body rather than instructions of the enclosing
    /// query. Completing an armed set operation is deliberately not attempted here - an
    /// operand scope is never a set operation's right side.
    /// </summary>
    public SubQueryInstruction PopOperand() => CloseScope();

    private SubQueryInstruction CloseScope()
    {
        var start = marks.Pop();
        var body = instructions.GetRange(start, instructions.Count - start);
        instructions.RemoveRange(start, instructions.Count - start);

        return new SubQueryInstruction(body);
    }

    public void From(string table, string? alias = null)
    {
        instructions.Add(new FromInstruction(table, alias));
    }

    /// <summary>
    /// The named intermediate results of the query in the order they were read (decision
    /// 112). They stand beside the instructions rather than among them, because a definition
    /// belongs to the whole query and a reader meets a derived table deep inside a scope it is
    /// still filling; the order is the order of dependencies, because a reader defines a
    /// derived table inside a body before it defines the body.
    /// </summary>
    private readonly List<WithInstruction> definitions = [];

    /// <summary>
    /// Records a named intermediate result of the query (decision 112): a WITH, a derived
    /// table named by its alias, a composed LINQ chain named by the variable or lambda
    /// parameter that holds its rows. The body is a scope the parser closed with
    /// <see cref="PopOperand"/>; a row source refers to the definition by passing its name
    /// to <see cref="From"/> or <see cref="Join"/> where a table would stand.
    /// </summary>
    public void Define(string name, SubQueryInstruction body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(body);
        definitions.Add(new WithInstruction(name, body));
    }

    /// <summary>
    /// Whether the query already defines an intermediate result of this name - what a reader
    /// asks before it reads a name as a table, and before it lifts a derived table whose
    /// alias would mean two things (decision 112).
    /// </summary>
    public bool Defines(string name) => definitions.Any(d => SameName(d.Name, name));

    private static bool SameName(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records one projected value with an optional alias (decision 107): a column, a
    /// column under an aggregate, the whole entity as the column <c>*</c>, or an
    /// expression. The parsers hand over the operand and do not build the instruction
    /// themselves.
    /// </summary>
    public void Project(QueryOperand operand, string? alias = null)
    {
        ArgumentNullException.ThrowIfNull(operand);
        instructions.Add(new ProjectInstruction(operand, alias));
    }

    /// <summary>
    /// The column shape of <see cref="Project(QueryOperand, string?)"/>: one projected
    /// column, optionally under an aggregate function, optionally over the distinct values
    /// of the column (decision 102) - the modifier is meaningful with a function only, and
    /// the gate refuses it over <c>*</c>.
    /// </summary>
    public void Project(string table, string attr, string? alias = null, string? function = null, bool distinct = false)
        => Project(QueryOperand.Column(table, attr, function, distinct), alias);

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

    /// <summary>
    /// Records one ordering key with its direction (decision 107): a column, a column under
    /// an aggregate, an expression, or - as a column operand without a table - the alias of
    /// a projection.
    /// </summary>
    public void OrderBy(QueryOperand key, bool asc = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        instructions.Add(new OrderByInstruction(key, asc));
    }

    /// <summary>The column shape of <see cref="OrderBy(QueryOperand, bool)"/>; a null table names a projection alias (decision 073).</summary>
    public void OrderBy(string? table, string attributeOrAlias, bool asc = true)
        => OrderBy(QueryOperand.Column(table, attributeOrAlias), asc);

    public void Having(ConditionNode condition)
    {
        instructions.Add(new HavingInstruction(condition));
    }

    /// <summary>
    /// Records the pagination of the current (sub)query scope in offset-then-limit normal
    /// form (decision 060). Parsers call this once per scope; a source shape that does not
    /// reduce to this form is theirs to refuse. Each count is a number the source stated or
    /// a parameter the caller binds (decision 085).
    /// </summary>
    public void Paginate(RowCount? offset, RowCount? limit)
    {
        if (offset is null && limit is null)
        {
            return;
        }

        instructions.Add(new PaginationInstruction(offset, limit));
    }

    /// <summary>
    /// Records the slice a host sets on the query object of a query whose reader has already
    /// closed its scope (decision 113) - SetFirstResult and SetMaxResults on what NHibernate's
    /// CreateSQLQuery or CreateQuery returned, setFirstResult and setMaxResults on JPA's
    /// createNativeQuery. The slice belongs to the query's own scope, where a slice in its text
    /// would stand, so it goes there; a second one there is the template's to refuse, as two
    /// slices of one scope are. Over a set operation the slice would cut the composed result,
    /// which the representation has no place for, and dropping it would return other rows, so
    /// it refuses (decision 070) - the sentence the JPQL reader says of setMaxResults over a
    /// union. Nothing to do when both counts are null.
    /// </summary>
    public void PaginateReadQuery(RowCount? offset, RowCount? limit)
    {
        if (offset is null && limit is null)
        {
            return;
        }

        if (instructions.Count != 1 || instructions[0] is not SubQueryInstruction read)
        {
            return;
        }

        var body = Unwrap(read.Instructions);
        if (body.Count > 0 && body[0] is SetOperationInstruction)
        {
            Report(
                ConversionRecordKind.Failure,
                "The slice set on the query object applies to the result of the set operation, which the query representation cannot carry, and dropping it would change which rows the query returns; no artifact was generated.",
                QueryFeature.Pagination);
            return;
        }

        instructions[0] = new SubQueryInstruction([.. body, new PaginationInstruction(offset, limit)]);
    }

    /// <summary>
    /// Records that the current (sub)query scope collapses duplicate rows of its final
    /// projection (decision 073). Idempotent, so a second call in the same scope needs no
    /// rule against it.
    /// </summary>
    public void Distinct()
    {
        instructions.Add(new DistinctInstruction());
    }

    public void SetOperation(SetOperationType operation)
    {
        // A DISTINCT recorded over a completed set operation is folded into it before the
        // operation becomes the left side of the next one (decision 073): the marker has no
        // scope of its own, and the identity it stands for is the template's to apply.
        FoldTrailingDistinct();

        // The left operand is the last closed scope - or a completed set operation, which is
        // how A UNION B EXCEPT C chains: the finished (A UNION B) becomes the left side.
        var left = instructions.Count > 0
            ? instructions[^1] switch
            {
                SubQueryInstruction subQuery => subQuery,
                SetOperationInstruction chained => new SubQueryInstruction([chained]),
                _ => null,
            }
            : null;

        if (left is null)
        {
            throw new InvalidOperationException("Set operation can only be initiated after a subquery has been defined. Use Push() to start a subquery and Pop() to end it.");
        }

        instructions.RemoveAt(instructions.Count - 1);
        pendingSetOperations.Push((operation, left, marks.Count));
    }

    /// <summary>
    /// Replaces a completed set operation followed by DISTINCT markers at the end of the
    /// instruction list with the operation the pair means (decision 073). On a refusal the
    /// operation stays as it was: the failure is already on the channel and no artifact
    /// will come out.
    /// </summary>
    private void FoldTrailingDistinct()
    {
        while (instructions.Count >= 2
               && instructions[^1] is DistinctInstruction
               && instructions[^2] is SetOperationInstruction over)
        {
            instructions.RemoveRange(instructions.Count - 2, 2);
            instructions.Add(DistinctOver(over) ?? over);
        }
    }

    /// <summary>
    /// The relational identities of DISTINCT over a set operation (decision 073). UNION,
    /// INTERSECT and EXCEPT already return a set, so the marker collapses nothing and is
    /// left out; DISTINCT over UNION ALL is UNION, a rewrite rule Q14 permits; DISTINCT over
    /// EXCEPT ALL is not EXCEPT - for A = {1, 1, 2} and B = {1} the first gives {1, 2} and
    /// the second {2} - and refuses. Each identity is a record, because the output text
    /// differs from the input even where the rows do not. Returns null on refusal.
    /// </summary>
    private SetOperationInstruction? DistinctOver(SetOperationInstruction operation)
    {
        switch (operation.OperationType)
        {
            case SetOperationType.Union:
            case SetOperationType.Intersect:
            case SetOperationType.Except:
                Report(
                    ConversionRecordKind.Convention,
                    $"DISTINCT over a {operation.OperationType} collapses nothing further, as the operation already returns a set; it was left out.",
                    QueryFeature.Projection);
                return operation;

            case SetOperationType.UnionAll:
                Report(
                    ConversionRecordKind.Convention,
                    "DISTINCT over a UNION ALL is a UNION; the operation was written as UNION.",
                    QueryFeature.SetOperation);
                return operation with { OperationType = SetOperationType.Union };

            default:
                Report(
                    ConversionRecordKind.Failure,
                    $"DISTINCT over a {operation.OperationType} is not the operation without ALL and has no form the query representation carries; no artifact was generated.",
                    QueryFeature.SetOperation);
                return null;
        }
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
    ///
    /// A construct the target's query language does not speak - declared so by the
    /// descriptor, or met at the point of emission - ends the attempt in the target's own
    /// language, and the query is written whole in the native SQL of the target's dialect
    /// instead (decision 113, <see cref="FallBack"/>).
    /// </summary>
    public List<ConversionSource> Build()
    {
        // A builder that was already refused - by its parser, before a single instruction
        // reached it - has nothing more to say. Running the steps over an empty body would
        // add a second record about one event, "the query carries no instructions" on top of
        // the reason it actually failed for, and discard the result all the same.
        if (refused)
        {
            return [];
        }

        // What the parser reported stays whatever happens below; what this attempt reports
        // describes the attempt's text, which the fallback discards with it.
        var attempt = records.Count;
        unspoken.Clear();

        var artifacts = BuildArtifacts();

        if (refused)
        {
            return [];
        }

        return unspoken.Count == 0 ? artifacts : FallBack(attempt);
    }

    /// <summary>
    /// The escape path of decision 113, filling the one decision 022 chose and nobody wrote.
    /// The query the target's language does not speak is written whole - definitions, body,
    /// parameters, never a piece of it - by the shared writer of the dialect's SQL over the
    /// same instructions, and the wrapper's builder hands that text to the framework's API
    /// for native queries; the signature of the method stays, so the levels of verification
    /// and the Advisor see the shape a translation in the target's language has. A target
    /// without such an API refuses, each construct named, as it did before the decision.
    ///
    /// The records of the abandoned attempt go with its text: a convention of the target's
    /// language says nothing about the native SQL. The parser's records stay, the fallback's
    /// own follow, and before them one record of kind Fallback per construct, naming the
    /// dialect the artifact is bound to from now on. A refusal of the fallback - a shape the
    /// API cannot take, a gate no target passes - refuses the query.
    /// </summary>
    private List<ConversionSource> FallBack(int attempt)
    {
        var gaps = unspoken.DistinctBy(gap => (gap.Feature, gap.Reason)).ToList();
        unspoken.Clear();

        var fallback = Descriptor.NativeSqlApi is null ? null : NativeSqlBuilder();
        if (fallback is null)
        {
            foreach (var gap in gaps)
            {
                Report(gap with
                {
                    Kind = ConversionRecordKind.Failure,
                    Reason = $"{gap.Reason}, and {Descriptor.Framework} has no API for a query in native SQL to fall back on; no artifact was generated.",
                });
            }

            return [];
        }

        records.RemoveRange(attempt, records.Count - attempt);

        fallback.writesNativeSql = true;
        fallback.EntityMaps = EntityMaps;
        fallback.QueryName = QueryName;
        fallback.instructions.AddRange(instructions);
        fallback.definitions.AddRange(definitions);

        var artifacts = fallback.Build();

        if (fallback.refused)
        {
            foreach (var record in fallback.Records)
            {
                Report(record);
            }

            return [];
        }

        foreach (var gap in gaps)
        {
            records.Add(gap with
            {
                Reason = $"{gap.Reason}, so the whole query was written in the native SQL of {Descriptor.Dialect} and handed to {Descriptor.NativeSqlApi}; the artifact is bound to that dialect.",
            });
        }

        foreach (var record in fallback.Records)
        {
            Report(record);
        }

        return artifacts;
    }

    private List<ConversionSource> BuildArtifacts()
    {
        if (instructions.Count == 0)
        {
            Report(ConversionRecordKind.Failure, "The query carries no instructions.");
            return [];
        }

        var body = Unwrap(instructions);

        // The intermediate results first (decision 112): whether the target writes them at
        // all, and the rules every definition is held to, before anything reads a column of
        // one; then the row of each, which every walk below resolves a reference through.
        // A target whose language has no intermediate result ends the attempt here and falls
        // back (decision 113): nothing below could write the definitions in it.
        if (!GateDefinitions() || unspoken.Count > 0)
        {
            return [];
        }

        DescribeDefinitions();

        // A string literal compared with a temporal column is the moment the source wrote
        // - T-SQL and HQL have no other spelling - and takes the column's scalar here, over
        // the whole query and before anything reads the comparisons (decision 024, the
        // direction the readers cannot cover). The bodies of the definitions are part of the
        // query, so they are typed with it.
        var typedDefinitions = new List<WithInstruction>(gatedDefinitions.Count);
        foreach (var definition in gatedDefinitions)
        {
            if (TypeTemporalLiterals(definition.Body, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase)) is not { } typed)
            {
                return [];
            }

            typedDefinitions.Add(definition with { Body = typed });
        }

        gatedDefinitions = typedDefinitions;

        if (!TypeTemporalLiterals(body, [], out body))
        {
            return [];
        }

        // The expressions of the whole query, typed once from the mapping representation
        // and held to the four rules of decision 107 - before the parameters, because a
        // parameter inside an expression takes its scalar from the position it stands in,
        // and before any scope is normalized, for the same reason the parameters are. A
        // function the target's language does not speak ends the attempt here (decision
        // 113): its visitor has no spelling to write.
        if (!GateExpressions(body) || unspoken.Count > 0)
        {
            return [];
        }

        // The parameters of the whole query, resolved once and before anything is rendered
        // (decision 083): a scope that never gets normalized - a subquery operand of a
        // condition the gate below refuses - must not be able to leave a parameter out of
        // the signature, and the order of the signature must not depend on the order the
        // text happens to be assembled in (S2).
        if (!ResolveParameters(body))
        {
            return [];
        }

        // DISTINCT over a set operation is whatever the identity says it is (decision 073).
        if (body.Count > 1 && body[0] is SetOperationInstruction over && body.Skip(1).All(i => i is DistinctInstruction))
        {
            var folded = DistinctOver(over);
            return folded is null ? [] : BuildSetOperation(folded);
        }

        if (body.Count == 1 && body[0] is SetOperationInstruction setOperation)
        {
            return BuildSetOperation(setOperation);
        }

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return [];
        }

        var artifact = Compose(clauses);
        return FinalizeQuery(clauses, artifact);
    }

    /// <summary>
    /// Runs the eight steps over one set of clauses. Separate from <see cref="Build"/> so
    /// that a builder overriding <see cref="BuildSetOperation"/> can render each operand
    /// through the same steps.
    /// </summary>
    protected QueryArtifact Compose(QueryClauses clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        ReportUnspokenFeatures(clauses);

        var artifact = new QueryArtifact();

        // Relational evaluation order, which is what a LINQ chain writes literally and what
        // SQL and HQL permute only on the surface. It is a data dependency, not a style: a
        // LINQ projection lambda binds a grouping after GroupBy and an element before it, so
        // the projection cannot be composed until grouping is known. Pagination comes last
        // because the slice is the last relational operator (decision 060).
        BuildSource(clauses, artifact);
        BuildJoins(clauses, artifact);
        BuildFilter(clauses, artifact);
        BuildGrouping(clauses, artifact);
        BuildPostFilter(clauses, artifact);
        BuildOrdering(clauses, artifact);
        BuildProjection(clauses, artifact);
        BuildPagination(clauses, artifact);

        return artifact;
    }

    /// <summary>
    /// Strips subquery wrappers that hold nothing but another subquery. Every scope a parser
    /// opens is closed by a Pop that wraps, so a set-operation operand routinely arrives
    /// double-wrapped; the wrapping carries no meaning of its own.
    /// </summary>
    protected static IReadOnlyList<QueryInstruction> Unwrap(IReadOnlyList<QueryInstruction> body)
        => body.Count == 1 && body[0] is SubQueryInstruction inner ? Unwrap(inner.Instructions) : body;

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

        // A subquery left in the body as an instruction has no position the representation
        // renders (decision 061 puts subqueries into condition operands), and emitting the
        // query without it would return different rows - so it refuses rather than being the
        // loss it used to be. No parser produces this shape any more.
        if (body.OfType<SubQueryInstruction>().Any())
        {
            Report(
                ConversionRecordKind.Failure,
                "A subquery stands in the body of the query, which is no position the representation renders; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
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

        // The only position a subquery renders in is a WHERE or HAVING operand
        // (decision 061). A join's ON clause is not one: LINQ joins take two key selectors
        // with no room for a subquery, and dropping the join instead would change which
        // rows come back.
        if (body.OfType<JoinInstruction>().Any(j => ContainsSubQuery(j.OnCondition)))
        {
            Report(
                ConversionRecordKind.Failure,
                "A subquery inside a join's ON condition has no position the representation renders; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
        }

        // At most one pagination per (sub)query scope (decision 060). Parsers emit one;
        // a second one has no defined composition, so it is refused rather than merged.
        var paginations = body.OfType<PaginationInstruction>().ToList();
        if (paginations.Count > 1)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The query carries {paginations.Count} pagination instructions; at most one is allowed.",
                QueryFeature.Pagination);
            return null;
        }

        var projections = body.OfType<ProjectInstruction>().ToList();
        var groupBys = body.OfType<GroupByInstruction>().ToList();
        var orderBys = body.OfType<OrderByInstruction>().ToList();

        // Rule Q8: grouping is mandatory when aggregates sit next to plain columns. A query
        // that is nothing but aggregates needs no grouping, so that case is not reported.
        if (groupBys.Count == 0
            && projections.Any(p => p.Operand.IsAggregate)
            && projections.Any(p => !p.Operand.IsAggregate))
        {
            Report(
                ConversionRecordKind.Incompleteness,
                "Aggregated and plain columns are projected together without a grouping (rule Q8).",
                QueryFeature.Grouping);
        }

        // An aggregate over the distinct values of the whole row - JPQL's count(distinct c),
        // a count of distinct rows - has no form as a single aggregate in any SQL target
        // (decision 102). Refused here once, so that six targets answer alike.
        if (projections.Any(p => p.Operand is { Distinct: true, Property: "*" }))
        {
            Report(
                ConversionRecordKind.Failure,
                "An aggregate over the distinct values of the whole row (COUNT(DISTINCT *)) has no form in any target; no artifact was generated.",
                QueryFeature.Aggregation);
            return null;
        }

        var distinct = body.OfType<DistinctInstruction>().Any();

        // DISTINCT over a projection of nothing but ungrouped aggregates collapses nothing:
        // the result is one row (decision 073). Left out with a record rather than carried,
        // so that no target has to write it into a shape where it means something else - a
        // LINQ Distinct().Count() counts distinct rows where SELECT DISTINCT COUNT(*) does not.
        if (distinct && groupBys.Count == 0 && projections.Count > 0 && projections.All(p => p.Operand.IsAggregate))
        {
            Report(
                ConversionRecordKind.Convention,
                "DISTINCT over a projection of nothing but ungrouped aggregates collapses nothing - the result is a single row; it was left out.",
                QueryFeature.Projection);
            distinct = false;
        }

        // Under DISTINCT every ordering key has to be a projected column or alias
        // (decision 073): T-SQL rejects the query otherwise, and LINQ has no way to name a
        // column the Select before Distinct() did not keep. A whole-entity projection keeps
        // every column, so the rule does not arise there.
        if (distinct && projections.Count > 0)
        {
            var unprojected = orderBys.FirstOrDefault(o => !projections.Any(p => Projects(p, o)));
            if (unprojected is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The ordering key '{unprojected.Operand}' is not among the projected columns, which DISTINCT requires - T-SQL rejects the query and LINQ cannot name the key after Distinct(); no artifact was generated.",
                    QueryFeature.Ordering);
                return null;
            }
        }

        // An expression projected without an alias has no name (decision 107): a target that
        // reads a column by name - Dapper, MyBatis's map, the anonymous type of a LINQ
        // projection - has nothing to read it under, and a name invented by the tool is
        // forbidden (decision 028). Refused here once, for every target alike.
        var nameless = projections.FirstOrDefault(p => p.Operand.IsExpression && p.Alias is null);
        if (nameless is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The projected expression '{nameless.Operand}' carries no alias, so no target could name the column it yields, and the tool does not invent one; no artifact was generated.",
                QueryFeature.Expression);
            return null;
        }

        return new QueryClauses
        {
            From = sources[0],
            Projections = projections,
            Joins = [.. body.OfType<JoinInstruction>()],
            Filter = Conjoin([.. body.OfType<SelectInstruction>().Select(i => i.Condition)]),
            GroupBys = groupBys,
            PostFilter = Conjoin([.. body.OfType<HavingInstruction>().Select(i => i.Condition)]),
            OrderBys = orderBys,
            Offset = paginations.FirstOrDefault()?.Offset,
            Limit = paginations.FirstOrDefault()?.Limit,
            Distinct = distinct,
        };
    }

    /// <summary>
    /// Whether the projection is the value the ordering names (decision 073): by alias - a
    /// column key without a table whose property is the alias -, by the plain column itself,
    /// or, since decision 107, by the same aggregate or expression written in both places.
    /// A plain column matches no aggregate: the aggregate projects its function's value,
    /// not the column.
    /// </summary>
    protected static bool Projects(ProjectInstruction projection, OrderByInstruction order)
    {
        var key = order.Operand;
        var projected = projection.Operand;

        if (key is { IsColumn: true, Table: null, IsAggregate: false }
            && string.Equals(projection.Alias, key.Property, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (projected is { IsColumn: true, IsAggregate: false } && key is { IsColumn: true, IsAggregate: false })
        {
            return (key.Table is null || string.Equals(projected.Table, key.Table, StringComparison.OrdinalIgnoreCase))
                   && string.Equals(projected.Property, key.Property, StringComparison.OrdinalIgnoreCase);
        }

        // An aggregate or an expression matches an aggregate or an expression written the
        // same way - the text of the operand is its structure, undecorated.
        return projected.IsAggregate == key.IsAggregate
               && (projected.IsAggregate || projected.IsExpression || key.IsExpression)
               && string.Equals(projected.ToString(), key.ToString(), StringComparison.OrdinalIgnoreCase);
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
                // The escape character of LIKE (decision 102): meaningful under LIKE only,
                // and exactly one character, which is what T-SQL and JPQL take - a longer
                // one the target would refuse at run time, without a record.
                if (comparison.Escape is { } escape)
                {
                    if (comparison.Operator is not ComparisonOperator.Like)
                    {
                        Report(
                            ConversionRecordKind.Failure,
                            $"An escape character stands only on a LIKE comparison; under {comparison.Operator} no target has a place for it; no artifact was generated.",
                            QueryFeature.Filtering);
                        return false;
                    }

                    if (escape.Length != 1)
                    {
                        Report(
                            ConversionRecordKind.Failure,
                            $"The escape character '{escape}' of a LIKE is not a single character, which is all a target takes; no artifact was generated.",
                            QueryFeature.Filtering);
                        return false;
                    }
                }

                // The same rule as for a projection above: no target aggregates over the
                // distinct values of the whole row (decision 102).
                if (comparison.Left.Distinct && comparison.Left.Property == "*"
                    || comparison.Right is { Distinct: true, Property: "*" })
                {
                    Report(
                        ConversionRecordKind.Failure,
                        "An aggregate over the distinct values of the whole row (COUNT(DISTINCT *)) has no form in any target; no artifact was generated.",
                        QueryFeature.Aggregation);
                    return false;
                }

                if (comparison.Operator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
                {
                    return true;
                }

                // EXISTS is a predicate over a subquery the way IS NULL is a predicate over
                // a column (decisions 002 and 061): the right operand is unused, the left
                // must be the subquery.
                if (comparison.Operator is ComparisonOperator.Exists)
                {
                    if (!comparison.Left.IsSubQuery)
                    {
                        Report(
                            ConversionRecordKind.Failure,
                            "An EXISTS whose operand is not a subquery cannot be rendered; no artifact was generated.",
                            QueryFeature.Subquery);
                        return false;
                    }

                    return true;
                }

                // A list of values stands only as IN's right side (decision 074); the
                // template holds the position so that the three targets refuse the same
                // trees because one place refuses them.
                if (comparison.Left.IsValueList)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        "A list of values stands only as the right side of IN, not as the left operand of a comparison; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                // A collection parameter is bound to the same position and by the same rule
                // (decision 083): it is a list of values the caller writes instead of the
                // query, so it stands where a list of values stands and nowhere else.
                if (comparison.Left.Parameter?.IsCollection == true)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        "A collection parameter stands only as the right side of IN, not as the left operand of a comparison; no artifact was generated.",
                        QueryFeature.QueryParameter);
                    return false;
                }

                if (comparison.Right is null)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"A comparison with operator {comparison.Operator} carries no right operand, so the condition cannot be rendered without changing which rows the query returns; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                if (comparison.Operator is ComparisonOperator.In)
                {
                    // IN carries two right sides: a subquery (decision 061) and a list of
                    // values (decision 074). Anything else is a tree no target renders.
                    if (comparison.Right.IsSubQuery)
                    {
                        return true;
                    }

                    if (comparison.Right.IsValueList)
                    {
                        return ValuesShareAScalar(comparison.Right.Values!);
                    }

                    // The third right side, since decision 083: a collection parameter, whose
                    // elements the caller supplies. A scalar parameter is not one - it binds a
                    // single value, which IN has no form for.
                    if (comparison.Right.IsParameter)
                    {
                        if (comparison.Right.Parameter!.IsCollection)
                        {
                            return true;
                        }

                        Report(
                            ConversionRecordKind.Failure,
                            "An IN whose right side is a parameter binding a single value has no representation; no artifact was generated.",
                            QueryFeature.QueryParameter);
                        return false;
                    }

                    Report(
                        ConversionRecordKind.Failure,
                        "An IN whose right side is neither a subquery, a list of values nor a collection parameter has no representation; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                if (comparison.Right.IsValueList)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"A list of values stands only as the right side of IN; as the operand of {comparison.Operator} it cannot be rendered; no artifact was generated.",
                        QueryFeature.Filtering);
                    return false;
                }

                if (comparison.Right.Parameter?.IsCollection == true)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"A collection parameter stands only as the right side of IN; as the operand of {comparison.Operator} it cannot be rendered; no artifact was generated.",
                        QueryFeature.QueryParameter);
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
    /// Whether the values IN enumerates share a scalar a target can type the list with
    /// (decision 074). One scalar always does; so does one numeric family - the exact one
    /// (integers with Decimal) or the floating one (integers with Float and Double) - because
    /// both C#'s best common type and T-SQL's type precedence widen an integer literal into
    /// either without changing its digits. Decimal with Float or Double does not: C# has no
    /// implicit conversion between them, and T-SQL would convert the decimal to float and
    /// compare a different value than the source wrote. Any other mix is a list whose type
    /// the model cannot state, and the LINQ target has no compilable form for it. A value
    /// whose scalar nobody recognized takes no part - it goes out verbatim, as a lone
    /// constant does (decision 024) - and neither does a parameter among the values
    /// (decision 102), whose scalar the parameter gate takes from the left side of IN.
    /// </summary>
    private bool ValuesShareAScalar(IReadOnlyList<QueryOperand> values)
    {
        var scalars = values
            .Where(v => v.Constant?.Type is not null)
            .Select(v => v.Constant!.Type!.Value)
            .Distinct()
            .ToList();

        if (scalars.Count <= 1
            || scalars.All(s => IsWholeNumber(s) || s is ScalarType.Decimal)
            || scalars.All(s => IsWholeNumber(s) || s is ScalarType.Float or ScalarType.Double))
        {
            return true;
        }

        Report(
            ConversionRecordKind.Failure,
            $"The values of an IN list mix the scalars {string.Join(" and ", scalars)}, which no target can type as one list; no artifact was generated.",
            QueryFeature.Filtering);
        return false;
    }

    /// <summary>Whether the scalar counts things: the four integer widths of decision 014.</summary>
    private static bool IsWholeNumber(ScalarType scalar)
        => scalar is ScalarType.Byte or ScalarType.Short or ScalarType.Int or ScalarType.Long;

    /* ---- intermediate results (decision 112) ----------------------------------------- */

    /// <summary>
    /// The definitions a builder renders, in order: held to the rules of the gate below,
    /// every column of a body named explicitly, and the bodies typed the way the rest of the
    /// query is. Empty until <see cref="Build"/> has run them through, and empty for a query
    /// that defines nothing.
    /// </summary>
    private List<WithInstruction> gatedDefinitions = [];

    /// <inheritdoc cref="gatedDefinitions"/>
    protected IReadOnlyList<WithInstruction> Definitions => gatedDefinitions;

    /// <summary>
    /// The row of each intermediate result as an entity that exists only inside this query
    /// (decision 112): a property and a column of one name for every projection of the
    /// body, typed by the gate from the body's scope. Two of them, because the template
    /// resolves names two ways - the parameter gate through stated mappings only
    /// (<see cref="EntityFor"/>), rendering through the naming convention as well
    /// (<see cref="RenderedEntityFor"/>) - and a column of a definition is typed from the
    /// columns its body reads in whichever way the walk asking resolves them.
    /// </summary>
    private readonly Dictionary<string, EntityMap> statedRows = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc cref="statedRows"/>
    private readonly Dictionary<string, EntityMap> renderedRows = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the name a row source carries is an intermediate result of the query rather than a table (decision 112).</summary>
    protected bool IsDefinition(string table) => Defines(table);

    /// <summary>
    /// The names of the columns of an intermediate result in the order its body projects
    /// them - what a target writes for a whole-row projection over one where the language has
    /// no other spelling of it (HQL refuses <c>select d</c> over a derived root).
    /// </summary>
    protected IReadOnlyList<string> ColumnsOf(string definition)
        => renderedRows.TryGetValue(definition, out var row)
            ? [.. row.PropertyMaps.Select(p => p.ColumnName!)]
            : [];

    /// <summary>
    /// The columns of the query's result in the order they come back (decision 113): the
    /// name each goes by - the alias, or a bare column's own name, as SQL names it - and the
    /// scalar the gate derives for it, with the operand it projects. What a target reads
    /// where it has to declare the row of a query whose text states no type: the class EF
    /// Core's SqlQuery materializes into, the scalars NHibernate's AddScalar reads. A set
    /// operation answers with its leftmost operand, which is where SQL takes the names from;
    /// the whole row of an intermediate result with the columns of its definition. Empty for
    /// a query over the whole of an entity, whose row is the entity. Valid once the gates of
    /// <see cref="Build"/> have run, which is when a step asks.
    /// </summary>
    protected IReadOnlyList<ResultColumn> ResultColumns()
    {
        var select = LeftmostSelect(Unwrap(instructions));
        var projections = select.OfType<ProjectInstruction>().ToList();

        if (projections.Count == 0)
        {
            var source = select.OfType<FromInstruction>().FirstOrDefault();
            return source is not null && renderedRows.TryGetValue(source.Table, out var row)
                ? [.. row.PropertyMaps.Select(p => new ResultColumn(p.ColumnName, p.Property.Type?.ScalarType, null))]
                : [];
        }

        var aliases = ScopeAliases(select, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), RenderedEntityFor);
        return [.. projections.Select(p => new ResultColumn(ColumnNameOf(p), ScalarOf(p.Operand, aliases), p.Operand))];
    }

    /// <summary>
    /// Describes the row of every definition, in order, so that a definition reading an
    /// earlier one finds its row already there. The maps hold what the template derives and
    /// nothing else: no key, no relation, no table of the database - they never leave the
    /// builder, and no entity artifact comes of them.
    /// </summary>
    private void DescribeDefinitions()
    {
        statedRows.Clear();
        renderedRows.Clear();

        foreach (var definition in definitions)
        {
            // A second definition of the name is the gate's to refuse; the first one stands.
            if (statedRows.ContainsKey(definition.Name))
            {
                continue;
            }

            var select = LeftmostSelect(Unwrap(definition.Body.Instructions));
            var stated = RowOf(definition.Name, select, ScopeAliases(select, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), EntityFor));
            var rendered = RowOf(definition.Name, select, ScopeAliases(select, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), RenderedEntityFor));

            statedRows[definition.Name] = stated;
            renderedRows[definition.Name] = rendered;
        }
    }

    private EntityMap RowOf(string name, IReadOnlyList<QueryInstruction> select, Dictionary<string, EntityMap> aliases)
    {
        var map = new EntityMap
        {
            Entity = new Entity { Name = name },
            Table = name,
        };

        foreach (var projection in select.OfType<ProjectInstruction>())
        {
            // A column without a name, or a second one of a name, is the gate's to refuse.
            if (ColumnNameOf(projection) is not { } column
                || map.PropertyMaps.Any(p => SameName(p.ColumnName!, column)))
            {
                continue;
            }

            var property = new Property
            {
                Name = column,
                Type = ScalarOf(projection.Operand, aliases) is { } scalar ? LangType.Scalar(scalar) : null,
            };

            map.Entity.Properties.Add(property);
            map.PropertyMaps.Add(new PropertyMap { Property = property, ColumnName = column });
        }

        return map;
    }

    /// <summary>
    /// The name a column of an intermediate result goes by: the alias, or for a bare column
    /// the column itself, which is how SQL names it. An aggregate or an expression without
    /// an alias has none.
    /// </summary>
    private static string? ColumnNameOf(ProjectInstruction projection)
        => projection.Alias
           ?? (projection.Operand is { IsColumn: true, IsAggregate: false } column && column.Property != "*" ? column.Property : null);

    /// <summary>
    /// The SELECT whose projections name the columns of a body: the body itself, or for a
    /// body that is a set operation its leftmost operand, which is where SQL takes the names
    /// of a set operation's columns from.
    /// </summary>
    private static IReadOnlyList<QueryInstruction> LeftmostSelect(IReadOnlyList<QueryInstruction> body)
    {
        while (body.Count > 0 && body[0] is SetOperationInstruction operation)
        {
            body = Unwrap(operation.Left.Instructions);
        }

        return body;
    }

    /// <summary>
    /// Holds every definition of the query to the rules of decision 112, in one place for
    /// every target (decision 023), and fills <see cref="Definitions"/> with what passed,
    /// each body with its columns named. A target whose query language cannot express an
    /// intermediate result falls back to native SQL (decision 113) - a definition left out
    /// would leave its row source naming a table that does not exist (decision 053), so it
    /// is never left out. Returns false when the query cannot be built, having reported why.
    /// </summary>
    private bool GateDefinitions()
    {
        gatedDefinitions = [];

        if (definitions.Count == 0)
        {
            return true;
        }

        if (!writesNativeSql && Descriptor.SupportOf(QueryFeature.IntermediateResult) == FactSupport.NotExpressible)
        {
            var named = string.Join(", ", definitions.Select(d => $"'{d.Name}'"));
            var what = definitions.Count == 1
                ? $"the intermediate result {named} as a source of rows - a common table expression or a derived table -"
                : $"the intermediate results {named} as sources of rows - common table expressions or derived tables -";
            ReportUnspoken(
                $"The query reads {what} which the query language of {Descriptor.Framework} cannot express",
                QueryFeature.IntermediateResult);
            return true;
        }

        var admitted = true;

        for (var i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];

            if (definitions.Take(i).Any(d => SameName(d.Name, definition.Name)))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"Two intermediate results of the query are named '{definition.Name}', and a row source finds a definition by its name; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            var body = Unwrap(definition.Body.Instructions);
            var read = Scopes(body).SelectMany(RowSourcesOf).ToList();

            if (read.Any(table => SameName(table, definition.Name)))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The intermediate result '{definition.Name}' reads itself, which is a recursive common table expression the query representation does not carry yet; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            if (definitions.Skip(i + 1).FirstOrDefault(later => read.Any(table => SameName(table, later.Name))) is { } forward)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The intermediate result '{definition.Name}' reads '{forward.Name}', which the query defines after it; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            if (ReadsOutside(body, new HashSet<string>(StringComparer.OrdinalIgnoreCase)) is { } outer)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The intermediate result '{definition.Name}' reads '{outer}', which it does not declare itself - a lateral reference to the query around it, which the query representation does not carry; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            var projections = LeftmostSelect(body).OfType<ProjectInstruction>().ToList();

            if (projections.Count == 0)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The intermediate result '{definition.Name}' projects the whole entity rather than naming its columns, and the columns of an intermediate result are its projections; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            if (projections.FirstOrDefault(p => ColumnNameOf(p) is null) is { } nameless)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The column '{nameless.Operand}' of the intermediate result '{definition.Name}' has no name, so the query around it could not name it either; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            var twice = projections
                .GroupBy(p => ColumnNameOf(p)!, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (twice is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The intermediate result '{definition.Name}' has two columns named '{twice.Key}', and the query around it names a column by its name; no artifact was generated.",
                    QueryFeature.IntermediateResult);
                admitted = false;
                continue;
            }

            gatedDefinitions.Add(definition with { Body = Named(definition.Body) });
        }

        return admitted;
    }

    /// <summary>
    /// The body with every column of its naming SELECT carrying its name as an alias, so that
    /// a target which requires one there - HQL refuses a CTE column without it - gets it where
    /// the source left the column to name itself. The meaning does not change: the alias is
    /// the name SQL gives the column anyway.
    /// </summary>
    private static SubQueryInstruction Named(SubQueryInstruction body)
    {
        var inner = Unwrap(body.Instructions);

        if (inner.Count > 0 && inner[0] is SetOperationInstruction operation)
        {
            return new SubQueryInstruction([operation with { Left = Named(operation.Left) }, .. inner.Skip(1)]);
        }

        return new SubQueryInstruction([.. inner.Select(instruction =>
            instruction is ProjectInstruction { Alias: null } projection
                ? projection with { Alias = ColumnNameOf(projection) }
                : instruction)]);
    }

    /// <summary>The names the row sources of one scope carry: its FROM and the right sides of its joins.</summary>
    private static IEnumerable<string> RowSourcesOf(IReadOnlyList<QueryInstruction> scope)
        => scope.OfType<FromInstruction>().Select(f => f.Table)
            .Concat(scope.OfType<JoinInstruction>().Select(j => j.RightTable));

    /// <summary>
    /// Every scope of a body: the body, the operands of a set operation, and every subquery
    /// standing as an operand - in a condition, a projection, an ordering key or inside an
    /// expression -, each with the scopes inside it.
    /// </summary>
    private static IEnumerable<IReadOnlyList<QueryInstruction>> Scopes(IReadOnlyList<QueryInstruction> body)
    {
        yield return body;

        foreach (var instruction in body)
        {
            IEnumerable<IReadOnlyList<QueryInstruction>> inner = instruction switch
            {
                SetOperationInstruction operation => Scopes(Unwrap(operation.Left.Instructions)).Concat(Scopes(Unwrap(operation.Right.Instructions))),
                SubQueryInstruction nested => Scopes(Unwrap(nested.Instructions)),
                _ => OperandsOf(instruction).Where(o => o.IsSubQuery).SelectMany(o => Scopes(Unwrap(o.SubQuery!.Instructions))),
            };

            foreach (var scope in inner)
            {
                yield return scope;
            }
        }
    }

    /// <summary>
    /// The first qualifier a body uses that none of its scopes declares around the use, or
    /// null - a definition sees nothing of the query around it (decision 112). A qualifier
    /// is declared by an alias of a row source, or by its name where the source wrote none.
    /// </summary>
    private static string? ReadsOutside(IReadOnlyList<QueryInstruction> body, HashSet<string> enclosing)
    {
        var declared = new HashSet<string>(enclosing, StringComparer.OrdinalIgnoreCase);

        void Declare(string table, string? alias)
        {
            declared.Add(alias ?? table);
            declared.Add(table);
            declared.Add(table.Split('.').LastOrDefault() ?? table);
        }

        foreach (var source in body.OfType<FromInstruction>())
        {
            Declare(source.Table, source.Alias);
        }

        foreach (var join in body.OfType<JoinInstruction>())
        {
            Declare(join.RightTable, join.RightTableAlias);
        }

        foreach (var instruction in body)
        {
            switch (instruction)
            {
                case SetOperationInstruction operation:
                    if ((ReadsOutside(Unwrap(operation.Left.Instructions), enclosing)
                         ?? ReadsOutside(Unwrap(operation.Right.Instructions), enclosing)) is { } fromOperand)
                    {
                        return fromOperand;
                    }

                    continue;

                case SubQueryInstruction nested:
                    if (ReadsOutside(Unwrap(nested.Instructions), declared) is { } fromNested)
                    {
                        return fromNested;
                    }

                    continue;

                case GroupByInstruction grouping when !declared.Contains(grouping.Table):
                    return grouping.Table;
            }

            foreach (var operand in OperandsOf(instruction))
            {
                if (operand.IsSubQuery)
                {
                    if (ReadsOutside(Unwrap(operand.SubQuery!.Instructions), declared) is { } fromSubQuery)
                    {
                        return fromSubQuery;
                    }
                }
                else if (operand is { IsColumn: true, Table: { } qualifier } && !declared.Contains(qualifier))
                {
                    return qualifier;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The operands one instruction stands over, without descending into a subquery: each
    /// operand of its projection, ordering key or condition tree, with the leaves of every
    /// expression and the operands of every condition inside a CASE. A subquery comes out as
    /// the operand it is, for the caller to descend into or not.
    /// </summary>
    private static IEnumerable<QueryOperand> OperandsOf(QueryInstruction instruction) => instruction switch
    {
        ProjectInstruction projection => Flatten(projection.Operand),
        OrderByInstruction order => Flatten(order.Operand),
        SelectInstruction filter => OperandsOf(filter.Condition),
        HavingInstruction postFilter => OperandsOf(postFilter.Condition),
        JoinInstruction join => OperandsOf(join.OnCondition),
        _ => [],
    };

    private static IEnumerable<QueryOperand> OperandsOf(ConditionNode? node) => node switch
    {
        ComparisonCondition comparison => Flatten(comparison.Left).Concat(comparison.Right is null ? [] : Flatten(comparison.Right)),
        LogicalCondition logical => logical.Operands.SelectMany(OperandsOf),
        NotCondition negation => OperandsOf(negation.Operand),
        _ => [],
    };

    private static IEnumerable<QueryOperand> Flatten(QueryOperand operand)
    {
        yield return operand;

        if (operand.Values is { } values)
        {
            foreach (var value in values)
            {
                yield return value;
            }
        }

        if (operand.Expression is { } expression)
        {
            foreach (var leaf in expression.Leaves().SelectMany(Flatten))
            {
                yield return leaf;
            }

            foreach (var inner in expression.Branches?.SelectMany(b => OperandsOf(b.When)) ?? [])
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// The clauses of a definition's body for a builder to compose (decision 112), or null.
    /// A body that is a set operation comes back in <paramref name="setOperation"/> for the
    /// builder to render as one, with a DISTINCT over it folded by the identity decision 073
    /// gives. An ordering without a slice is dropped with a record: T-SQL does not allow it
    /// in a common table expression or a derived table, and the order of an intermediate
    /// result's rows does not change which rows the query over it returns - the sentence
    /// decision 061 said of a subquery. Null without <paramref name="setOperation"/> means
    /// the body cannot be rendered, and the reason is on the channel.
    /// </summary>
    protected QueryClauses? NormalizeDefinition(WithInstruction definition, out SetOperationInstruction? setOperation)
    {
        ArgumentNullException.ThrowIfNull(definition);

        setOperation = null;
        var body = Unwrap(definition.Body.Instructions);

        if (body.Count > 0 && body[0] is SetOperationInstruction operation && body.Skip(1).All(i => i is DistinctInstruction))
        {
            setOperation = body.Count > 1 ? DistinctOver(operation) : operation;
            return null;
        }

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return null;
        }

        if (clauses.OrderBys.Count > 0 && clauses.Offset is null && clauses.Limit is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The ordering inside the intermediate result '{definition.Name}' has no slice to decide, and the order of an intermediate result's rows does not change which rows the query over it returns; it was dropped.",
                QueryFeature.Ordering);

            clauses = WithoutOrdering(clauses);
        }

        return clauses;
    }

    /// <summary>The same clauses without their ordering keys.</summary>
    protected static QueryClauses WithoutOrdering(QueryClauses clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);

        return new QueryClauses
        {
            From = clauses.From,
            Projections = clauses.Projections,
            Joins = clauses.Joins,
            Filter = clauses.Filter,
            GroupBys = clauses.GroupBys,
            PostFilter = clauses.PostFilter,
            OrderBys = [],
            Offset = clauses.Offset,
            Limit = clauses.Limit,
            Distinct = clauses.Distinct,
        };
    }

    /// <summary>
    /// The definition a column operand reads through its qualifier, or null for a column
    /// of a table: the qualifier is an alias whose row the template described for an
    /// intermediate result.
    /// </summary>
    private WithInstruction? DefinitionBehind(QueryOperand column, Dictionary<string, EntityMap> aliases)
    {
        if (column is not { IsColumn: true, Table: { } qualifier })
        {
            return null;
        }

        var row = aliases.GetValueOrDefault(qualifier) ?? (statedRows.TryGetValue(qualifier, out var named) ? named : null);
        return row is null
            ? null
            : definitions.FirstOrDefault(d => statedRows.TryGetValue(d.Name, out var described) && ReferenceEquals(described, row));
    }

    /// <summary>
    /// The demand behind one column of an intermediate result (decision 105 over decision
    /// 112): the table behind the column of the definition's body that the column is, in the
    /// body's own scope - a COUNT asks for nothing, an expression for the tables of its
    /// columns, a column of an earlier definition for what stands behind that one.
    /// </summary>
    private void DemandDefinitionColumn(WithInstruction definition, string column, List<QueryTableDemand> found, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visited.Add(definition.Name))
        {
            return;
        }

        var select = LeftmostSelect(Unwrap(definition.Body.Instructions));
        var projection = select.OfType<ProjectInstruction>()
            .FirstOrDefault(p => ColumnNameOf(p) is { } name && SameName(name, column));
        if (projection is null)
        {
            return;
        }

        var scopeAliases = ScopeAliases(select, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), EntityFor);
        var scopeUnbound = UnboundTables(select, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        if (TableTheGateCannotResolve(projection.Operand, ComparisonOperator.Equal, scopeAliases, scopeUnbound) is { } table)
        {
            Demand(table, found);
        }
        else if (projection.Operand.IsExpression)
        {
            DemandColumnsOf(projection.Operand.Expression!, scopeAliases, scopeUnbound, found);
        }
        else if (!string.Equals(projection.Operand.Function, "COUNT", StringComparison.OrdinalIgnoreCase)
                 && DefinitionBehind(projection.Operand, scopeAliases) is { } inner)
        {
            DemandDefinitionColumn(inner, projection.Operand.Property!, found, visited);
        }
    }

    /* ---- parameters (decision 083) -------------------------------------------------- */

    private readonly List<QueryParameter> parameters = [];

    /// <summary>
    /// The parameters of the generated method, in order of first occurrence, each carrying
    /// the scalar the gate resolved (decision 083). Computed from the condition trees rather
    /// than declared on the query: the same name is the same value, so the list is
    /// derivable, and a stored one could only drift from the tree it describes.
    /// </summary>
    protected IReadOnlyList<QueryParameter> Parameters => parameters;

    /// <summary>
    /// The names the query uses for nothing but a row count of its pagination
    /// (decision 085), which is what tells the two kinds of target apart below.
    /// </summary>
    private readonly HashSet<string> rowCountOnly = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether the target writes a bound row count into the query text. The two targets
    /// whose queries are SQL do - <c>FETCH NEXT @take</c> is part of the statement - while
    /// the three that take the slice on the query object do not, and a LINQ chain captures
    /// its values instead of binding them at all (decision 085).
    /// </summary>
    protected virtual bool WritesRowCountsIntoQueryText => true;

    /// <summary>
    /// The parameters a target binds into its query by name, which is every parameter of
    /// the generated method except a row count the query text never names: binding that one
    /// would name a parameter the query does not have, and both NHibernate and JPA throw on
    /// it rather than ignore it. A name used as a row count <em>and</em> in a condition is
    /// in the text all the same, so it stays.
    ///
    /// The signature is <see cref="Parameters"/> either way - the count is still an argument
    /// the caller supplies, only one that reaches the query through SetMaxResults.
    /// </summary>
    protected IEnumerable<QueryParameter> BoundParameters
        => WritesRowCountsIntoQueryText
            ? Parameters
            : Parameters.Where(p => !rowCountOnly.Contains(KeyOf(p)));

    /// <summary>
    /// Whether the target's query language spells a positional parameter (decision 083).
    /// Only JPQL does; in every other target a positional parameter comes out named after
    /// its order, which is a recorded convention rather than a loss - the query binds the
    /// same value, only by another road.
    /// </summary>
    protected virtual bool WritesPositionalParameters => false;

    /// <summary>
    /// Resolves every parameter of the query into <see cref="Parameters"/> (decision 083).
    /// The shared gate the template holds so that all targets answer the same way: the
    /// parser reads what the source wrote, and the question "can this be typed and bound?"
    /// is asked once here, exactly as decision 074 asked its question about IN lists and
    /// decision 053 about the condition tree.
    ///
    /// Here and not in the parser because the scalar comes from the <em>mapping</em> IR,
    /// which only a builder has (<see cref="EntityMaps"/>, handed over by the orchestration
    /// before generating). Returns false when the query cannot be built, having reported why.
    /// </summary>
    private bool ResolveParameters(IReadOnlyList<QueryInstruction> body)
    {
        // Build is repeatable, so the list is rebuilt rather than appended to: a second call
        // used to double every parameter of the signature.
        parameters.Clear();
        rowCountOnly.Clear();

        // The definitions before the body, because that is where the text of every target
        // that writes them puts them - WITH before the statement, the variables before the
        // chain - and the order of the signature is the order of first occurrence (S2).
        var occurrences = new List<ParameterOccurrence>();
        foreach (var definition in gatedDefinitions)
        {
            CollectParameters(Unwrap(definition.Body.Instructions), new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), occurrences);
        }

        CollectParameters(body, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase), occurrences);

        if (occurrences.Count == 0)
        {
            return true;
        }

        // The one target that has both forms - JPQL - does not take them in one query, and
        // every other target has only the named form, so one of the two would have to be
        // invented. Refused rather than mixed (decision 083).
        if (occurrences.Any(o => o.Parameter.Name is not null) && occurrences.Any(o => o.Parameter.IsPositional))
        {
            Report(
                ConversionRecordKind.Failure,
                "The query mixes named and positional parameters, which no target's query language binds together; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        var order = new List<string>();
        var grouped = new Dictionary<string, List<ParameterOccurrence>>(StringComparer.Ordinal);

        foreach (var occurrence in occurrences)
        {
            var key = KeyOf(occurrence.Parameter);
            if (!grouped.TryGetValue(key, out var group))
            {
                grouped[key] = group = [];
                order.Add(key);
            }

            group.Add(occurrence);
        }

        var resolved = true;

        foreach (var key in order)
        {
            if (Resolve(grouped[key], out var parameter))
            {
                parameters.Add(parameter!);
            }
            else
            {
                resolved = false;
            }
        }

        if (!resolved)
        {
            return false;
        }

        if (!WritesPositionalParameters && parameters.Any(p => p.IsPositional))
        {
            var named = parameters.Where(p => p.IsPositional).Select(QueryParameterNaming.IdentifierFor);
            Report(
                ConversionRecordKind.Convention,
                $"The target's query language has no positional parameter, so the positional parameters were named after their order ({string.Join(", ", named)}); the query binds the same values.",
                QueryFeature.QueryParameter);
        }

        return true;
    }

    /// <summary>
    /// One parameter, from every place the query names it. The same name is the same value
    /// and therefore one parameter of the method; two occurrences that imply different
    /// scalars are refused rather than unified, because unifying them would make one of the
    /// two comparisons compare something other than what the source wrote.
    /// </summary>
    private bool Resolve(List<ParameterOccurrence> group, out QueryParameter? parameter)
    {
        parameter = null;
        var first = group[0].Parameter;
        var named = Describe(first);

        // A name that is not a plain identifier - a MyBatis property path, say - has no form
        // in the signature of a method, and the model does not carry what the path means.
        if (first.Name is { } name && !IsPlainIdentifier(name))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The parameter {named} is not a plain identifier, so no target can name it in the signature of the generated method; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        if (group.Any(o => o.Parameter.IsCollection != first.IsCollection))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The parameter {named} stands once as a list of values and once as a single value, which is one name for two bindings; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        var stated = group.Select(o => o.Parameter.Type).Where(t => t is not null).Distinct().ToList();
        if (stated.Count > 1)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The source states two scalars for the parameter {named} ({string.Join(" and ", stated)}); no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        if (group.Any(o => o.IsRowCount))
        {
            return ResolveRowCount(group, first, named, stated, out parameter);
        }

        var derived = group.Select(o => o.Derived).Where(t => t is not null).Distinct().ToList();

        if (stated.Count == 0 && derived.Count > 1)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The parameter {named} is compared against values of two scalars ({string.Join(" and ", derived)}), so the generated method cannot type it; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        var scalar = stated.Count == 1 ? stated[0] : derived.Count == 1 ? derived[0] : null;
        if (scalar is null)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The scalar of the parameter {named} does not follow from what it is compared against, so the generated method cannot type it; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        // Two sources of one fact are never reconciled in silence - the answer decision 015
        // gives when the catalog and the mapping disagree.
        if (stated.Count == 1 && derived.Any(d => d != stated[0]))
        {
            Report(
                ConversionRecordKind.Conflict,
                $"The source states the scalar {stated[0]} for the parameter {named} while the comparison implies {string.Join(" and ", derived)}; the stated one was kept.",
                QueryFeature.QueryParameter);
        }

        parameter = first.WithType(scalar.Value);
        return true;
    }

    /// <summary>
    /// One parameter that stands as a row count of a pagination (decision 085). Its scalar
    /// is <see cref="ScalarType.Int"/> and the clause decides it, not a comparison and not
    /// the source: Skip, Take, SetFirstResult, SetMaxResults, setFirstResult and
    /// setMaxResults all bind 32 bits, and T-SQL takes an int wherever it takes a bigint, so
    /// one scalar is what lets one model yield one signature in every direction (S2).
    ///
    /// Which is why the precedence of decision 083 does not apply here: there a stated
    /// scalar outranks a scalar <em>derived</em> from the other side of a comparison, and a
    /// clause is not a derivation.
    /// </summary>
    private bool ResolveRowCount(
        List<ParameterOccurrence> group,
        QueryParameter first,
        string named,
        List<ScalarType?> stated,
        out QueryParameter? parameter)
    {
        parameter = null;

        // A row count is one number, so a parameter that binds a list is not one. Reachable
        // rather than hypothetical: MyBatis's foreach writes itself into the text as a
        // one-element IN list (decision 084) and could land in a FETCH clause.
        if (first.IsCollection)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The parameter {named} binds a list of values and stands as a row count of the pagination, which is one number; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        // Where the same name also stands in a condition, the two places have to agree, and
        // the row count says Int. Unifying them would make one of the two mean something
        // other than what the source wrote - the rule decision 083 states for two derived
        // scalars, reaching the case it did not have yet.
        var elsewhere = group
            .Where(o => !o.IsRowCount)
            .Select(o => o.Derived)
            .Where(t => t is not null and not ScalarType.Int)
            .Distinct()
            .ToList();

        if (elsewhere.Count > 0)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The parameter {named} stands as a row count of the pagination, which is {ScalarType.Int}, and is compared against values of {string.Join(" and ", elsewhere)}; the generated method cannot type it; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        // MyBatis is the one source that states a scalar of its own (decision 084). A row
        // count that is not a whole number is a contradiction between what the source says
        // the value is and the position it wrote it in, and emitting either reading would
        // bind something other than what the source declared.
        if (stated.Count == 1 && !IsWholeNumber(stated[0]!.Value))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The source states the scalar {stated[0]} for the parameter {named}, which stands as a row count of the pagination and can only be a whole number; no artifact was generated.",
                QueryFeature.QueryParameter);
            return false;
        }

        // A wider whole number the source stated is carried no further: the generated method
        // takes the count as Int, which is a fact the source stated and the artifact does not
        // use - a loss, and not a conflict, because the two sides here are the source and the
        // limit of the targets, not two sources of one fact.
        if (stated.Count == 1 && stated[0] != ScalarType.Int)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The source states the scalar {stated[0]} for the parameter {named}, which stands as a row count of the pagination; the generated method takes it as {ScalarType.Int}, which is the width every target's pagination binds.",
                QueryFeature.QueryParameter);
        }

        // A name the query uses for nothing else is in no target's query text where the
        // slice lives on the query object, so it is not bound there by name.
        if (group.All(o => o.IsRowCount))
        {
            rowCountOnly.Add(KeyOf(first));
        }

        parameter = first.WithType(ScalarType.Int);
        return true;
    }

    /// <summary>
    /// Walks one scope for parameters, carrying the aliases of the scopes around it so that
    /// a column named inside a subquery still finds the entity it belongs to. The order of
    /// the walk is the order of the signature: instructions as recorded, the left operand
    /// of a comparison before the right one (decision 083).
    /// </summary>
    private void CollectParameters(
        IReadOnlyList<QueryInstruction> body,
        Dictionary<string, EntityMap> enclosing,
        List<ParameterOccurrence> found)
    {
        var aliases = ScopeAliases(body, enclosing, EntityFor);

        foreach (var instruction in body)
        {
            switch (instruction)
            {
                // A projection or an ordering key carries a parameter only inside an
                // expression (decision 107), which types it from the columns beside it.
                case ProjectInstruction projection:
                    CollectParameters(projection.Operand, null, ComparisonOperator.Equal, aliases, found);
                    break;
                case OrderByInstruction order:
                    CollectParameters(order.Operand, null, ComparisonOperator.Equal, aliases, found);
                    break;
                case SelectInstruction filter:
                    CollectParameters(filter.Condition, aliases, found);
                    break;
                case HavingInstruction postFilter:
                    CollectParameters(postFilter.Condition, aliases, found);
                    break;
                case JoinInstruction join:
                    CollectParameters(join.OnCondition, aliases, found);
                    break;
                case SubQueryInstruction nested:
                    CollectParameters(Unwrap(nested.Instructions), aliases, found);
                    break;
                case SetOperationInstruction operation:
                    CollectParameters(Unwrap(operation.Left.Instructions), aliases, found);
                    CollectParameters(Unwrap(operation.Right.Instructions), aliases, found);
                    break;
            }
        }

        // The row counts of this scope are collected after its conditions, whatever order
        // the parser recorded them in (decision 085). TOP sits inside the SELECT clause and
        // Take at the end of a LINQ chain, so a signature that followed the recorded order
        // would come out differently for two sources of one query, and the same model has to
        // yield the same method (S2). Offset before limit, which is the normal form.
        foreach (var pagination in body.OfType<PaginationInstruction>())
        {
            CollectRowCount(pagination.Offset, found);
            CollectRowCount(pagination.Limit, found);
        }
    }

    /// <summary>
    /// A bound row count as one occurrence of a parameter (decision 085). The scalar it
    /// contributes comes from the clause and not from a comparison - there is no other side
    /// to a pagination - and it is <see cref="ScalarType.Int"/>, which is the width every
    /// target's pagination API binds. It enters as a derived scalar, so that a name used
    /// both here and in a condition goes through the same unification as any other.
    /// </summary>
    private static void CollectRowCount(RowCount? count, List<ParameterOccurrence> found)
    {
        if (count?.Parameter is { } parameter)
        {
            found.Add(new ParameterOccurrence(parameter, ScalarType.Int, IsRowCount: true));
        }
    }

    private void CollectParameters(
        ConditionNode? node,
        Dictionary<string, EntityMap> aliases,
        List<ParameterOccurrence> found)
    {
        switch (node)
        {
            case ComparisonCondition comparison:
                CollectParameters(comparison.Left, comparison.Right, comparison.Operator, aliases, found);
                CollectParameters(comparison.Right, comparison.Left, comparison.Operator, aliases, found);
                return;
            case LogicalCondition logical:
                foreach (var operand in logical.Operands)
                {
                    CollectParameters(operand, aliases, found);
                }

                return;
            case NotCondition negation:
                CollectParameters(negation.Operand, aliases, found);
                return;
        }
    }

    private void CollectParameters(
        QueryOperand? operand,
        QueryOperand? other,
        ComparisonOperator op,
        Dictionary<string, EntityMap> aliases,
        List<ParameterOccurrence> found)
    {
        if (operand is null)
        {
            return;
        }

        if (operand.IsSubQuery)
        {
            CollectParameters(Unwrap(operand.SubQuery!.Instructions), aliases, found);
            return;
        }

        if (operand.IsParameter)
        {
            found.Add(new ParameterOccurrence(operand.Parameter!, ScalarOfOther(other, op, aliases)));
            return;
        }

        // A parameter among the values of an IN list (decision 102) takes its scalar from
        // the left side of IN, exactly as a collection parameter's element does - never
        // from the constants beside it.
        if (operand.IsValueList)
        {
            foreach (var value in operand.Values!)
            {
                if (value.IsParameter)
                {
                    found.Add(new ParameterOccurrence(value.Parameter!, ScalarOfOther(other, op, aliases)));
                }
            }

            return;
        }

        // A parameter inside an expression takes its scalar from the position it stands in
        // (decision 107): the table of that decision, with the other side of the comparison
        // the expression stands in as the context an Abs hands down.
        if (operand.IsExpression)
        {
            CollectParameters(operand.Expression!, ScalarOfOther(other, op, aliases), aliases, found);
        }
    }

    /// <summary>
    /// The parameters inside one expression, each with the scalar its position implies
    /// (decision 107): a string in a concatenation, the scalar of the other side in an
    /// arithmetic operation, the scalar a function takes in its argument, the common scalar
    /// of the typed siblings in a COALESCE or among the branches of a CASE, and for Abs the
    /// scalar of the comparison the call stands in. Where the position implies nothing -
    /// <c>@a + @b</c> - the occurrence carries none and the parameter gate refuses it with
    /// the sentence it has for every parameter it cannot type.
    /// </summary>
    private void CollectParameters(
        QueryExpression expression,
        ScalarType? context,
        Dictionary<string, EntityMap> aliases,
        List<ParameterOccurrence> found)
    {
        void Leaf(QueryOperand leaf, ScalarType? implied)
        {
            if (leaf.IsParameter)
            {
                found.Add(new ParameterOccurrence(leaf.Parameter!, implied));
            }
            else if (leaf.IsExpression)
            {
                CollectParameters(leaf.Expression!, implied, aliases, found);
            }
            else if (leaf.IsSubQuery)
            {
                CollectParameters(Unwrap(leaf.SubQuery!.Instructions), aliases, found);
            }
        }

        if (expression.IsBinary)
        {
            var (left, right) = (expression.Left!, expression.Right!);
            if (IsConcatenation(expression, aliases))
            {
                Leaf(left, ScalarType.String);
                Leaf(right, ScalarType.String);
                return;
            }

            Leaf(left, ScalarOf(right, aliases));
            Leaf(right, ScalarOf(left, aliases));
            return;
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!;
            switch (expression.Function!.Value)
            {
                case QueryFunction.Upper:
                case QueryFunction.Lower:
                case QueryFunction.Trim:
                case QueryFunction.Length:
                case QueryFunction.EscapePattern:
                    Leaf(arguments[0], ScalarType.String);
                    return;

                case QueryFunction.Substring:
                    Leaf(arguments[0], ScalarType.String);
                    Leaf(arguments[1], ScalarType.Int);
                    Leaf(arguments[2], ScalarType.Int);
                    return;

                case QueryFunction.Year:
                case QueryFunction.Month:
                case QueryFunction.Day:
                    Leaf(arguments[0], ScalarType.DateTime);
                    return;

                case QueryFunction.Abs:
                    Leaf(arguments[0], context);
                    return;

                case QueryFunction.Coalesce:
                    for (var i = 0; i < arguments.Count; i++)
                    {
                        var siblings = arguments.Where((_, j) => j != i).Select(a => ScalarOf(a, aliases));
                        Leaf(arguments[i], CommonScalar(siblings, out _));
                    }

                    return;

                case QueryFunction.CurrentTimestamp:
                    return;

                default:
                    throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null);
            }
        }

        var values = expression.Leaves().ToList();
        foreach (var branch in expression.Branches!)
        {
            CollectParameters(branch.When, aliases, found);
        }

        for (var i = 0; i < values.Count; i++)
        {
            var siblings = values.Where((_, j) => j != i).Select(v => ScalarOf(v, aliases));
            Leaf(values[i], CommonScalar(siblings, out _));
        }
    }

    /* ---- the query's demand on the catalog (decision 105) ---------------------------- */

    /// <summary>
    /// The tables this query needs bound to an entity before it can be built, as the query
    /// names them (decision 105). The gate above types a parameter from the column on the
    /// other side of its comparison and follows the column's qualifier to an entity through
    /// a stated mapping only (<see cref="EntityFor"/>) - never through the naming convention
    /// of decision 050, which <see cref="AliasedEntities"/> and the typing of a literal do
    /// use. A qualifier the stated mappings do not resolve is the one case the gate cannot
    /// answer without the catalog, and it is known exactly once the query is read: this
    /// walks the scopes the way
    /// <see cref="CollectParameters(IReadOnlyList{QueryInstruction}, Dictionary{string, EntityMap}, List{ParameterOccurrence})"/>
    /// does and yields the table behind every such qualifier, once each, in order of first
    /// occurrence. A comparison with a subquery asks for the table behind the one value the
    /// subquery projects, in the subquery's own scope. What it leaves out is exactly what the
    /// gate never asks the mapping for: a LIKE pattern, a comparison with a constant, a COUNT
    /// - inside a subquery or not -, a row count of the pagination, and an unqualified
    /// column, which the gate looks up across every entity of the conversion. Empty for a
    /// query without parameters, so a conversion whose queries need nothing reads nothing.
    ///
    /// Data rather than a call (decision 015): the orchestration hands the demand to the
    /// catalog component between reading and <see cref="Build"/>, and the builder never
    /// touches a database. Every target inherits it and none implements it (S1).
    /// </summary>
    public IReadOnlyList<QueryTableDemand> CatalogDemand()
    {
        var demands = new List<QueryTableDemand>();

        // The rows of the intermediate results resolve a reference to one as a stated
        // mapping, so that nobody asks the catalog for a table named after a definition, and
        // their bodies are walked like any scope (decision 112).
        DescribeDefinitions();
        foreach (var definition in definitions)
        {
            CollectDemands(
                Unwrap(definition.Body.Instructions),
                new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                demands);
        }

        CollectDemands(
            Unwrap(instructions),
            new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            demands);

        return demands;
    }

    private void CollectDemands(
        IReadOnlyList<QueryInstruction> body,
        Dictionary<string, EntityMap> enclosingAliases,
        Dictionary<string, string> enclosingUnbound,
        List<QueryTableDemand> found)
    {
        var aliases = ScopeAliases(body, enclosingAliases, EntityFor);
        var unbound = UnboundTables(body, enclosingUnbound);

        foreach (var instruction in body)
        {
            switch (instruction)
            {
                case ProjectInstruction projection:
                    CollectDemands(projection.Operand, null, ComparisonOperator.Equal, aliases, unbound, found);
                    break;
                case OrderByInstruction order:
                    CollectDemands(order.Operand, null, ComparisonOperator.Equal, aliases, unbound, found);
                    break;
                case SelectInstruction filter:
                    CollectDemands(filter.Condition, aliases, unbound, found);
                    break;
                case HavingInstruction postFilter:
                    CollectDemands(postFilter.Condition, aliases, unbound, found);
                    break;
                case JoinInstruction join:
                    CollectDemands(join.OnCondition, aliases, unbound, found);
                    break;
                case SubQueryInstruction nested:
                    CollectDemands(Unwrap(nested.Instructions), aliases, unbound, found);
                    break;
                case SetOperationInstruction operation:
                    CollectDemands(Unwrap(operation.Left.Instructions), aliases, unbound, found);
                    CollectDemands(Unwrap(operation.Right.Instructions), aliases, unbound, found);
                    break;
            }
        }
    }

    /// <summary>
    /// The tables of one scope no stated mapping resolves, keyed by the name a column
    /// qualifies with - the alias, or the table itself where the source wrote none - on top
    /// of the enclosing scopes'. The counterpart of <see cref="ScopeAliases"/> for the
    /// names it left out.
    /// </summary>
    private Dictionary<string, string> UnboundTables(IReadOnlyList<QueryInstruction> body, Dictionary<string, string> enclosing)
    {
        var unbound = new Dictionary<string, string>(enclosing, StringComparer.OrdinalIgnoreCase);

        foreach (var source in body.OfType<FromInstruction>())
        {
            if (EntityFor(source.Table) is null)
            {
                unbound[source.Alias ?? source.Table] = source.Table;
            }
        }

        foreach (var join in body.OfType<JoinInstruction>())
        {
            if (EntityFor(join.RightTable) is null)
            {
                unbound[join.RightTableAlias ?? join.RightTable] = join.RightTable;
            }
        }

        return unbound;
    }

    private void CollectDemands(
        ConditionNode? node,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound,
        List<QueryTableDemand> found)
    {
        switch (node)
        {
            case ComparisonCondition comparison:
                CollectDemands(comparison.Left, comparison.Right, comparison.Operator, aliases, unbound, found);
                CollectDemands(comparison.Right, comparison.Left, comparison.Operator, aliases, unbound, found);
                return;
            case LogicalCondition logical:
                foreach (var operand in logical.Operands)
                {
                    CollectDemands(operand, aliases, unbound, found);
                }

                return;
            case NotCondition negation:
                CollectDemands(negation.Operand, aliases, unbound, found);
                return;
        }
    }

    private void CollectDemands(
        QueryOperand? operand,
        QueryOperand? other,
        ComparisonOperator op,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound,
        List<QueryTableDemand> found)
    {
        if (operand is null)
        {
            return;
        }

        if (operand.IsSubQuery)
        {
            CollectDemands(Unwrap(operand.SubQuery!.Instructions), aliases, unbound, found);
            return;
        }

        // An expression walks its own leaves (decision 107): a parameter inside it takes its
        // scalar from the columns beside it, so those columns' tables are demanded where no
        // stated mapping binds them - and the other side of the comparison as well, because
        // the context an Abs hands down comes from there.
        if (operand.IsExpression)
        {
            CollectDemands(operand.Expression!, other, op, aliases, unbound, found);
            return;
        }

        // A parameter, or a parameter among the values of an IN list (decision 102) - the
        // two shapes the gate types from the other side of the comparison.
        var binds = operand.IsParameter || (operand.IsValueList && operand.Values!.Any(value => value.IsParameter));
        if (!binds)
        {
            return;
        }

        if (TableTheGateCannotResolve(other, op, aliases, unbound) is { } table)
        {
            Demand(table, found);
        }

        // The other side is a column of an intermediate result: the scalar comes from the
        // column of the definition's body it names (decision 112).
        if (op is not ComparisonOperator.Like
            && !string.Equals(other?.Function, "COUNT", StringComparison.OrdinalIgnoreCase)
            && other is not null && DefinitionBehind(other, aliases) is { } definition)
        {
            DemandDefinitionColumn(definition, other.Property!, found);
        }

        // The other side is an expression: the parameter's scalar comes from it, and so from
        // every column it stands over.
        if (other?.IsExpression == true)
        {
            DemandColumnsOf(other.Expression!, aliases, unbound, found);
        }

        // The other side is a subquery: the scalar comes from the one value it projects.
        if (other?.IsSubQuery == true && op is not ComparisonOperator.Like)
        {
            DemandProjectionOf(other.SubQuery!, aliases, unbound, found);
        }
    }

    private void CollectDemands(
        QueryExpression expression,
        QueryOperand? other,
        ComparisonOperator op,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound,
        List<QueryTableDemand> found)
    {
        if (ContainsParameter(expression))
        {
            DemandColumnsOf(expression, aliases, unbound, found);

            if (op is not ComparisonOperator.Like && TableTheGateCannotResolve(other, op, aliases, unbound) is { } table)
            {
                Demand(table, found);
            }

            if (op is not ComparisonOperator.Like && other?.IsSubQuery == true)
            {
                DemandProjectionOf(other.SubQuery!, aliases, unbound, found);
            }
        }

        foreach (var leaf in expression.Leaves())
        {
            if (leaf.IsSubQuery)
            {
                CollectDemands(Unwrap(leaf.SubQuery!.Instructions), aliases, unbound, found);
            }
            else if (leaf.IsExpression)
            {
                CollectDemands(leaf.Expression!, other, op, aliases, unbound, found);
            }
        }

        if (expression.Branches is { } branches)
        {
            foreach (var branch in branches)
            {
                CollectDemands(branch.When, aliases, unbound, found);
            }
        }
    }

    /// <summary>
    /// The tables of every qualified column inside the expression that no stated mapping
    /// resolves, demanded once each - a subquery among the leaves included, whose one
    /// projection types it.
    /// </summary>
    private void DemandColumnsOf(
        QueryExpression expression,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound,
        List<QueryTableDemand> found)
    {
        foreach (var leaf in expression.Leaves())
        {
            if (leaf.IsColumn && leaf.Table is not null && leaf.Property != "*"
                && !aliases.ContainsKey(leaf.Table) && EntityFor(leaf.Table) is null
                && unbound.GetValueOrDefault(leaf.Table) is { } table)
            {
                Demand(table, found);
            }
            else if (leaf.IsColumn && leaf.Property != "*" && !leaf.IsAggregate && DefinitionBehind(leaf, aliases) is { } definition)
            {
                DemandDefinitionColumn(definition, leaf.Property!, found);
            }
            else if (leaf.IsExpression)
            {
                DemandColumnsOf(leaf.Expression!, aliases, unbound, found);
            }
            else if (leaf.IsSubQuery)
            {
                DemandProjectionOf(leaf.SubQuery!, aliases, unbound, found);
            }
        }
    }

    /// <summary>
    /// The table behind the one value a subquery projects, where the gate types a parameter
    /// from it and no stated mapping binds it: the mirror of
    /// <see cref="ScalarOf(SubQueryInstruction, Dictionary{string, EntityMap})"/>, in the
    /// subquery's own scope. A COUNT asks for nothing, as it does outside a subquery.
    /// </summary>
    private void DemandProjectionOf(
        SubQueryInstruction subQuery,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound,
        List<QueryTableDemand> found)
    {
        var body = Unwrap(subQuery.Instructions);
        if (SingleProjection(body) is not { } projection)
        {
            return;
        }

        var scopeAliases = ScopeAliases(body, aliases, EntityFor);
        var scopeUnbound = UnboundTables(body, unbound);

        if (TableTheGateCannotResolve(projection, ComparisonOperator.Equal, scopeAliases, scopeUnbound) is { } table)
        {
            Demand(table, found);
        }
        else if (projection.IsExpression)
        {
            DemandColumnsOf(projection.Expression!, scopeAliases, scopeUnbound, found);
        }
        else if (!string.Equals(projection.Function, "COUNT", StringComparison.OrdinalIgnoreCase)
                 && DefinitionBehind(projection, scopeAliases) is { } definition)
        {
            DemandDefinitionColumn(definition, projection.Property!, found);
        }
    }

    private static void Demand(string table, List<QueryTableDemand> found)
    {
        var demand = QueryTableDemand.Of(table);
        if (!found.Any(demand.Names))
        {
            found.Add(demand);
        }
    }

    /// <summary>Whether the expression holds a parameter anywhere short of a subquery.</summary>
    private static bool ContainsParameter(QueryExpression expression)
        => expression.Leaves().Any(leaf => leaf.IsParameter || (leaf.IsExpression && ContainsParameter(leaf.Expression!)));

    /// <summary>
    /// The mirror of <see cref="ScalarOfOther"/> and <see cref="ScalarOfColumn"/>: the table
    /// behind a qualified column the gate would look up and not find, or null where the gate
    /// never asks the mapping (a LIKE pattern, a constant, a COUNT, an unqualified column),
    /// where the other side is not a column (an expression or a subquery, which their callers
    /// look into) or where it would find the answer in a stated mapping.
    /// </summary>
    private string? TableTheGateCannotResolve(
        QueryOperand? other,
        ComparisonOperator op,
        Dictionary<string, EntityMap> aliases,
        Dictionary<string, string> unbound)
    {
        if (op is ComparisonOperator.Like || other is null || !other.IsColumn || other.Table is null)
        {
            return null;
        }

        if (string.Equals(other.Function, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (aliases.ContainsKey(other.Table) || EntityFor(other.Table) is not null)
        {
            return null;
        }

        return unbound.GetValueOrDefault(other.Table);
    }

    /// <summary>
    /// The scalar a parameter takes from the other side of its comparison (decision 083).
    /// A second parameter says nothing, and neither does a constant whose own scalar nobody
    /// recognized; each of those ends as a refusal above, because a parameter of a method
    /// has to have a type.
    /// </summary>
    private ScalarType? ScalarOfOther(QueryOperand? other, ComparisonOperator op, Dictionary<string, EntityMap> aliases)
    {
        // A LIKE pattern is a string whichever side of the operator it stands on, and
        // whatever the column it matches is typed as (decision 051).
        if (op is ComparisonOperator.Like)
        {
            return ScalarType.String;
        }

        if (other is null)
        {
            return null;
        }

        // An expression answers with the scalar the gate derives for it (decision 107), and
        // a subquery with the scalar of the one value it projects (decision 061).
        if (other.IsExpression || other.IsSubQuery)
        {
            return ScalarOf(other, aliases);
        }

        if (!other.IsColumn)
        {
            return other.Constant?.Type;
        }

        // COUNT answers with a count, not in the type of what it counted; the other
        // aggregates answer in the type of their column.
        return string.Equals(other.Function, "COUNT", StringComparison.OrdinalIgnoreCase)
            ? ScalarType.Long
            : ScalarOfColumn(aliases, other.Table, other.Property!);
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// The typed view of the expressions of this query, filled by the gate below on every
    /// Build and read by the visitors (decision 107): what the mapping representation says
    /// an expression's scalar is, and which <c>+</c> stands over a string.
    /// </summary>
    protected ExpressionTyping Expressions { get; } = new();

    /// <summary>
    /// The scalar of an operand as the gate derives it (decision 107): a column's from the
    /// mapping, a constant's as the parser read it, a parameter's as the source stated it, an
    /// expression's from its leaves by the table of the decision, a subquery's from the one
    /// value it projects; a list says nothing. An aggregate over any of them answers as an
    /// aggregate over a column does - COUNT with a count, the others in the scalar of their
    /// argument.
    /// </summary>
    private ScalarType? ScalarOf(QueryOperand operand, Dictionary<string, EntityMap> aliases)
    {
        ScalarType? argument;
        if (operand.IsExpression)
        {
            argument = ScalarOf(operand.Expression!, aliases);
        }
        else if (operand.IsSubQuery)
        {
            argument = ScalarOf(operand.SubQuery!, aliases);
        }
        else if (operand.IsColumn)
        {
            argument = operand.Property == "*" ? null : ScalarOfColumn(aliases, operand.Table, operand.Property!);
        }
        else if (operand.IsConstant)
        {
            argument = operand.Constant!.Type;
        }
        else if (operand.IsParameter)
        {
            argument = operand.Parameter!.Type;
        }
        else
        {
            argument = null;
        }

        return string.Equals(operand.Function, "COUNT", StringComparison.OrdinalIgnoreCase) ? ScalarType.Long : argument;
    }

    private ScalarType? ScalarOf(QueryExpression expression, Dictionary<string, EntityMap> aliases)
    {
        if (expression.IsBinary)
        {
            if (IsConcatenation(expression, aliases))
            {
                return ScalarType.String;
            }

            return CommonScalar([ScalarOf(expression.Left!, aliases), ScalarOf(expression.Right!, aliases)], out _);
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!;
            return expression.Function!.Value switch
            {
                QueryFunction.Upper or QueryFunction.Lower or QueryFunction.Trim or QueryFunction.Substring
                    or QueryFunction.EscapePattern => ScalarType.String,
                QueryFunction.Length or QueryFunction.Year or QueryFunction.Month or QueryFunction.Day => ScalarType.Int,
                QueryFunction.Abs => ScalarOf(arguments[0], aliases),
                QueryFunction.Coalesce => CommonScalar(arguments.Select(a => ScalarOf(a, aliases)), out _),
                QueryFunction.CurrentTimestamp => ScalarType.DateTime,
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        return CommonScalar(expression.Leaves().Select(v => ScalarOf(v, aliases)), out _);
    }

    /// <summary>
    /// The scalar of a subquery standing as an operand: the scalar of the one value it
    /// projects, which is what a scalar comparison compares and what IN ranges over (decision
    /// 061) - COUNT with a count, SUM, MIN, MAX and AVG in the scalar of what they aggregate,
    /// an expression by the table of decision 107. The projection is typed in the subquery's
    /// own scope on top of the enclosing one, so that a correlated column still finds its
    /// entity, and that scope is resolved through stated mappings only unless the caller says
    /// otherwise, as the parameter gate resolves (decisions 083 and 105): the typed view of
    /// the expressions, which resolves wider, may find a subquery over a table only the naming
    /// convention binds untyped, which leaves a scalar out and never gives another; the typing
    /// of a literal passes its own, wider resolution. A body without exactly one projection -
    /// a set operation, the whole entity, several columns - answers nothing, and the operand
    /// is refused for that by its own rule (<see cref="NormalizeSubQueryOperand"/>).
    /// </summary>
    private ScalarType? ScalarOf(
        SubQueryInstruction subQuery,
        Dictionary<string, EntityMap> aliases,
        Func<string, EntityMap?>? resolve = null)
    {
        var body = Unwrap(subQuery.Instructions);
        return SingleProjection(body) is { } projection
            ? ScalarOf(projection, ScopeAliases(body, aliases, resolve ?? EntityFor))
            : null;
    }

    /// <summary>The operand of the one projection of a scope, or null where it projects none or several.</summary>
    private static QueryOperand? SingleProjection(IReadOnlyList<QueryInstruction> body)
    {
        var projections = body.OfType<ProjectInstruction>().Take(2).ToList();
        return projections.Count == 1 ? projections[0].Operand : null;
    }

    /// <summary>
    /// Whether a binary expression concatenates (decision 107): by its operator, or an
    /// <c>Add</c> one of whose sides the mapping types as a string - the overloaded <c>+</c>
    /// of T-SQL and C#, which the readers cannot decide without types and the gate can.
    /// </summary>
    private bool IsConcatenation(QueryExpression expression, Dictionary<string, EntityMap> aliases)
    {
        if (expression.Operator == ExpressionOperator.Concat)
        {
            return true;
        }

        if (expression.Operator != ExpressionOperator.Add)
        {
            return false;
        }

        return ScalarOf(expression.Left!, aliases) is ScalarType.String or ScalarType.Char
               || ScalarOf(expression.Right!, aliases) is ScalarType.String or ScalarType.Char;
    }

    /// <summary>
    /// The one scalar a set of scalars unifies to by the rule of decision 074 for the
    /// values of an IN list: one scalar, or one numeric family - the exact one (integers
    /// with Decimal, which is Decimal) or the floating one (integers with Float and Double,
    /// which is Double). Scalars nobody derived take no part. <paramref name="unified"/> is
    /// false where two typed scalars do not unify - Decimal with a floating-point number,
    /// a string with a number -, and the result is then null.
    /// </summary>
    private static ScalarType? CommonScalar(IEnumerable<ScalarType?> scalars, out bool unified)
    {
        var typed = scalars.Where(s => s is not null).Select(s => s!.Value).Distinct().ToList();
        unified = true;

        if (typed.Count == 0)
        {
            return null;
        }

        if (typed.Count == 1)
        {
            return typed[0];
        }

        if (typed.All(s => IsWholeNumber(s) || s is ScalarType.Decimal))
        {
            return typed.Contains(ScalarType.Decimal) ? ScalarType.Decimal : WidestWholeNumber(typed);
        }

        if (typed.All(s => IsWholeNumber(s) || s is ScalarType.Float or ScalarType.Double))
        {
            return typed.Any(s => s is ScalarType.Float or ScalarType.Double) ? ScalarType.Double : WidestWholeNumber(typed);
        }

        unified = false;
        return null;
    }

    private static ScalarType WidestWholeNumber(IEnumerable<ScalarType> scalars)
        => scalars.Contains(ScalarType.Long) ? ScalarType.Long
            : scalars.Contains(ScalarType.Int) ? ScalarType.Int
            : scalars.Contains(ScalarType.Short) ? ScalarType.Short
            : ScalarType.Byte;

    /// <summary>
    /// The gate over the expressions of the whole query (decision 107), one for every target
    /// (decision 023). Walks every scope the way the parameter gate does and holds each
    /// expression to four rules: it is typed from the mapping, and refused by name where the
    /// spelling depends on a type nobody derived - a <c>+</c> with no typed side, a COALESCE
    /// or a CASE over scalars that do not unify, Decimal with a floating-point number in
    /// arithmetic; a function the target's descriptor does not speak is refused (rule Q14 at
    /// the grain of a function); an aggregate over an aggregate is refused, as no target
    /// writes it. The fourth rule, an expression projected without an alias, needs the
    /// clauses of a scope and sits in <see cref="Normalize"/>. What the gate derives goes
    /// into <see cref="Expressions"/> for the visitors. Returns false when the query cannot
    /// be built, having reported why.
    /// </summary>
    private bool GateExpressions(IReadOnlyList<QueryInstruction> body)
    {
        Expressions.Clear();

        // The bodies of the intermediate results are scopes of the query like any other
        // (decision 112); each stands on its own, seeing nothing of the query around it.
        var admitted = true;
        foreach (var definition in gatedDefinitions)
        {
            admitted &= GateExpressions(Unwrap(definition.Body.Instructions), new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase));
        }

        return GateExpressions(body, new Dictionary<string, EntityMap>(StringComparer.OrdinalIgnoreCase)) && admitted;
    }

    private bool GateExpressions(IReadOnlyList<QueryInstruction> body, Dictionary<string, EntityMap> enclosing)
    {
        // Resolved the way rendering resolves (AliasedEntities), through the naming
        // convention of decision 050 as well: the typed view is what the visitors write from,
        // and a column of a source that states no table renders as the property its class
        // declares. The parameter gate keeps its own, narrower resolution.
        var aliases = ScopeAliases(body, enclosing, RenderedEntityFor);
        var admitted = true;

        foreach (var instruction in body)
        {
            switch (instruction)
            {
                case ProjectInstruction projection:
                    admitted &= GateOperand(projection.Operand, aliases);
                    break;
                case OrderByInstruction order:
                    admitted &= GateOperand(order.Operand, aliases);
                    break;
                case SelectInstruction filter:
                    admitted &= GateExpressions(filter.Condition, aliases);
                    break;
                case HavingInstruction postFilter:
                    admitted &= GateExpressions(postFilter.Condition, aliases);
                    break;
                case JoinInstruction join:
                    admitted &= GateExpressions(join.OnCondition, aliases);
                    break;
                case SubQueryInstruction nested:
                    admitted &= GateExpressions(Unwrap(nested.Instructions), aliases);
                    break;
                case SetOperationInstruction operation:
                    admitted &= GateExpressions(Unwrap(operation.Left.Instructions), aliases);
                    admitted &= GateExpressions(Unwrap(operation.Right.Instructions), aliases);
                    break;
            }
        }

        return admitted;
    }

    private bool GateExpressions(ConditionNode? node, Dictionary<string, EntityMap> aliases)
    {
        switch (node)
        {
            case ComparisonCondition comparison:
                var left = GateOperand(comparison.Left, aliases);
                var right = comparison.Right is null || GateOperand(comparison.Right, aliases);
                return left && right;
            case LogicalCondition logical:
                return logical.Operands.Aggregate(true, (admitted, operand) => GateExpressions(operand, aliases) && admitted);
            case NotCondition negation:
                return GateExpressions(negation.Operand, aliases);
            default:
                return true;
        }
    }

    private bool GateOperand(QueryOperand operand, Dictionary<string, EntityMap> aliases)
    {
        if (operand.IsSubQuery)
        {
            return GateExpressions(Unwrap(operand.SubQuery!.Instructions), aliases);
        }

        if (operand.IsValueList)
        {
            return operand.Values!.Aggregate(true, (admitted, value) => GateOperand(value, aliases) && admitted);
        }

        if (!operand.IsExpression)
        {
            return true;
        }

        var admitted = true;

        // No target writes an aggregate over an aggregate; SUM(COUNT(*)) is not SQL.
        if (operand.IsAggregate && ContainsAggregate(operand.Expression!))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The aggregate {operand.Function} stands over '{operand.Expression}', which carries an aggregate itself, and no target writes an aggregate over an aggregate; no artifact was generated.",
                QueryFeature.Expression);
            admitted = false;
        }

        return GateExpression(operand.Expression!, aliases) && admitted;
    }

    private bool GateExpression(QueryExpression expression, Dictionary<string, EntityMap> aliases)
    {
        var admitted = true;

        foreach (var leaf in expression.Leaves())
        {
            admitted &= GateOperand(leaf, aliases);
        }

        if (expression.Branches is { } branches)
        {
            foreach (var branch in branches)
            {
                admitted &= GateExpressions(branch.When, aliases);
            }
        }

        // A function the descriptor leaves out is not invented a spelling for (decision 107);
        // the target writes the query in native SQL instead, or refuses (decision 113).
        if (expression.IsCall && !writesNativeSql && !Descriptor.Speaks(expression.Function!.Value))
        {
            ReportUnspoken(
                $"The function {expression.Function} is not one the query language of {Descriptor.Framework} speaks, and the tool does not invent a spelling for it",
                QueryFeature.Expression);
        }

        var concatenates = IsConcatenation(expression, aliases);
        ScalarType? scalar;

        if (expression.IsBinary && !concatenates)
        {
            var sides = new[] { ScalarOf(expression.Left!, aliases), ScalarOf(expression.Right!, aliases) };
            scalar = CommonScalar(sides, out var unified);

            if (expression.Operator == ExpressionOperator.Add && sides.All(s => s is null))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The operator + stands between '{expression.Left}' and '{expression.Right}', neither of which the mapping types, so the gate cannot tell an addition from a concatenation and two targets would spell it differently; no artifact was generated.",
                    QueryFeature.Expression);
                admitted = false;
            }
            else if (!unified)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The arithmetic '{expression}' mixes the scalars {string.Join(" and ", sides.Where(s => s is not null).Distinct())}, which no target types as one value; no artifact was generated.",
                    QueryFeature.Expression);
                admitted = false;
            }
        }
        else if (expression.IsCall && expression.Function == QueryFunction.Coalesce)
        {
            scalar = CommonScalar(expression.Arguments!.Select(a => ScalarOf(a, aliases)), out var unified);
            if (!unified)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The arguments of '{expression}' have scalars that do not unify into one, so the value has no type any target could give it; no artifact was generated.",
                    QueryFeature.Expression);
                admitted = false;
            }
        }
        else if (expression.IsCase)
        {
            scalar = CommonScalar(expression.Leaves().Select(v => ScalarOf(v, aliases)), out var unified);
            if (!unified)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The branches of '{expression}' have scalars that do not unify into one, so the value has no type any target could give it; no artifact was generated.",
                    QueryFeature.Expression);
                admitted = false;
            }
        }
        else
        {
            scalar = ScalarOf(expression, aliases);
        }

        Expressions.Record(expression, scalar, concatenates);
        return admitted;
    }

    /// <summary>Whether the expression stands over an aggregate anywhere short of a subquery.</summary>
    private static bool ContainsAggregate(QueryExpression expression)
        => expression.Leaves().Any(leaf => leaf.IsAggregate || (leaf.IsExpression && ContainsAggregate(leaf.Expression!)));

    private ScalarType? ScalarOfColumn(Dictionary<string, EntityMap> aliases, string? table, string column)
    {
        if (table is not null)
        {
            var qualified = aliases.GetValueOrDefault(table) ?? EntityFor(table);
            return qualified is null ? null : ScalarIn(qualified, column);
        }

        // Unqualified: the entities this scope names answer first and the whole conversion
        // after them, which is the widening order EntityFor uses.
        return aliases.Values.Select(map => ScalarIn(map, column)).FirstOrDefault(s => s is not null)
               ?? EntityMaps.Select(map => ScalarIn(map, column)).FirstOrDefault(s => s is not null);
    }

    /// <summary>
    /// The scalar of the property a column maps, matched the way <see cref="PropertyFor"/>
    /// matches it. A property whose language type nobody stated, or whose type is not a
    /// scalar, answers nothing: that is the gap decision 075 reports, not a type to guess at.
    /// </summary>
    private static ScalarType? ScalarIn(EntityMap map, string column)
        => map.PropertyMaps
            .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
            ?.Property.Type is { Category: LangTypeCategory.Scalar } type
            ? type.ScalarType
            : null;

    /// <summary>
    /// The entity behind every alias one scope names - its source and its joins - on top of
    /// the aliases of the scopes around it, so that a column inside a subquery still finds
    /// the entity it belongs to. The resolution is the caller's, because the two walks over
    /// the query resolve differently: the scalar gate of decision 083 takes a stated mapping
    /// only, the typing of a literal the same map the column renders from. An alias the
    /// scope declares hides the enclosing one of the same name even where the resolution
    /// finds no entity for it, as it does in SQL: a column of the inner table would otherwise
    /// be typed from the outer one.
    /// </summary>
    private static Dictionary<string, EntityMap> ScopeAliases(
        IReadOnlyList<QueryInstruction> body,
        Dictionary<string, EntityMap> enclosing,
        Func<string, EntityMap?> resolve)
    {
        var aliases = new Dictionary<string, EntityMap>(enclosing, StringComparer.OrdinalIgnoreCase);

        void Declare(string alias, string table)
        {
            if (resolve(table) is { } entity)
            {
                aliases[alias] = entity;
            }
            else
            {
                aliases.Remove(alias);
            }
        }

        foreach (var source in body.OfType<FromInstruction>())
        {
            Declare(source.Alias ?? source.Table, source.Table);
        }

        foreach (var join in body.OfType<JoinInstruction>())
        {
            Declare(join.RightTableAlias ?? join.RightTable, join.RightTable);
        }

        return aliases;
    }

    // ---- Temporal literals -------------------------------------------------------------

    /// <summary>
    /// Types the string literals a source compares with a temporal column (decision 024,
    /// in the direction the readers cannot cover). T-SQL and HQL write a moment as a string
    /// - <c>o.PlacedAt > '2025-01-01'</c> - and their grammars cannot tell it from a string,
    /// so the readers carry it as <see cref="ScalarType.String"/>; four targets write the
    /// string back and let the database convert it, LINQ writes a comparison of a date with
    /// a string, which does not compile. The scalar of the column is what the gate of
    /// decision 083 already derives for a parameter, from the mapping representation only
    /// the builder has, so the same derivation types the constant: a string compared with a
    /// DateTime, Date or TimeOfDay column - or with a subquery projecting one - becomes that
    /// scalar in the ISO spelling every visitor writes, and a string that does not read as
    /// one is refused - the database would refuse it too, at run time and without a record.
    /// A string against a column of any other scalar stays a string.
    ///
    /// Walks every scope the way <see cref="CollectParameters(IReadOnlyList{QueryInstruction}, Dictionary{string, EntityMap}, List{ParameterOccurrence})"/>
    /// does, and resolves a column the way rendering does (<see cref="AliasedEntities"/>):
    /// through the naming convention of decision 050 as well, because a source that states
    /// no table - Dapper, MyBatis - renders the column as the property its class declares,
    /// and the literal has to agree with that property. The parameter gate keeps its own,
    /// narrower resolution: a scalar in a method signature is a claim about the caller and
    /// follows from a stated mapping only. Returns false when a literal was refused, having
    /// reported why.
    /// </summary>
    private bool TypeTemporalLiterals(
        IReadOnlyList<QueryInstruction> body,
        Dictionary<string, EntityMap> enclosing,
        out IReadOnlyList<QueryInstruction> typed)
    {
        var aliases = ScopeAliases(body, enclosing, RenderedEntityFor);
        var result = new List<QueryInstruction>(body.Count);
        typed = result;

        foreach (var instruction in body)
        {
            QueryInstruction? replacement = instruction switch
            {
                SelectInstruction filter =>
                    TypeTemporalLiterals(filter.Condition, aliases) is { } condition ? filter with { Condition = condition } : null,
                HavingInstruction postFilter =>
                    TypeTemporalLiterals(postFilter.Condition, aliases) is { } condition ? postFilter with { Condition = condition } : null,
                JoinInstruction join =>
                    TypeTemporalLiterals(join.OnCondition, aliases) is { } condition ? join with { OnCondition = condition } : null,
                SubQueryInstruction nested => TypeTemporalLiterals(nested, aliases),
                SetOperationInstruction operation =>
                    TypeTemporalLiterals(operation.Left, aliases) is { } left && TypeTemporalLiterals(operation.Right, aliases) is { } right
                        ? operation with { Left = left, Right = right }
                        : null,
                _ => instruction,
            };

            if (replacement is null)
            {
                return false;
            }

            result.Add(replacement);
        }

        return true;
    }

    private SubQueryInstruction? TypeTemporalLiterals(SubQueryInstruction subQuery, Dictionary<string, EntityMap> enclosing)
        => TypeTemporalLiterals(subQuery.Instructions, enclosing, out var typed)
            ? subQuery with { Instructions = [.. typed] }
            : null;

    private ConditionNode? TypeTemporalLiterals(ConditionNode node, Dictionary<string, EntityMap> aliases)
    {
        switch (node)
        {
            case ComparisonCondition comparison:
                return TypeTemporalLiterals(comparison, aliases);

            case LogicalCondition logical:
                var operands = new List<ConditionNode>(logical.Operands.Count);
                foreach (var operand in logical.Operands)
                {
                    if (TypeTemporalLiterals(operand, aliases) is not { } typed)
                    {
                        return null;
                    }

                    operands.Add(typed);
                }

                return logical with { Operands = operands };

            case NotCondition negation:
                return TypeTemporalLiterals(negation.Operand, aliases) is { } inner ? negation with { Operand = inner } : null;

            default:
                return node;
        }
    }

    private ComparisonCondition? TypeTemporalLiterals(ComparisonCondition comparison, Dictionary<string, EntityMap> aliases)
    {
        var left = TypeTemporalLiteral(comparison.Left, comparison.Right, comparison.Operator, aliases);
        if (left is null)
        {
            return null;
        }

        if (comparison.Right is null)
        {
            return comparison with { Left = left };
        }

        var right = TypeTemporalLiteral(comparison.Right, comparison.Left, comparison.Operator, aliases);
        return right is null ? null : comparison with { Left = left, Right = right };
    }

    /// <summary>
    /// One operand: a subquery has its own scopes walked; a string constant, or a list of
    /// nothing but string constants (the right side of IN, decision 074), takes the scalar
    /// of the temporal column on the other side. Everything else passes unchanged.
    /// </summary>
    private QueryOperand? TypeTemporalLiteral(
        QueryOperand operand,
        QueryOperand? other,
        ComparisonOperator op,
        Dictionary<string, EntityMap> aliases)
    {
        if (operand.IsSubQuery)
        {
            return TypeTemporalLiterals(operand.SubQuery!, aliases) is { } typed ? QueryOperand.Nested(typed) : null;
        }

        if (TemporalScalarOf(other, op, aliases) is not { } scalar)
        {
            return operand;
        }

        if (operand.Constant is { Type: ScalarType.String } constant)
        {
            return Retype(constant, scalar, other!) is { } typed ? QueryOperand.Value(typed, operand.Function) : null;
        }

        // A parameter among the values (decision 102) is left as it is - its scalar comes
        // from the column through the parameter gate - and the constants beside it are typed.
        if (operand.Values is { } list
            && list.Any(v => v.IsConstant)
            && list.All(v => v.IsParameter || v.Constant?.Type == ScalarType.String))
        {
            var values = new List<QueryOperand>(list.Count);
            foreach (var value in list)
            {
                if (value.IsParameter)
                {
                    values.Add(value);
                    continue;
                }

                if (Retype(value.Constant!, scalar, other!) is not { } typed)
                {
                    return null;
                }

                values.Add(QueryOperand.Value(typed));
            }

            return QueryOperand.ValueList(values);
        }

        return operand;
    }

    /// <summary>
    /// The temporal scalar of the column on the other side of a comparison, or of the one
    /// value a subquery there projects (decision 061) - <c>(SELECT MAX(o.PlacedAt) …) &gt;
    /// '2025-01-01'</c> compares a moment as much as the column does. Null when there is no
    /// such value: the other side is neither, is COUNT (which answers with a count), or its
    /// scalar is another one or none. The subquery's scope is resolved the way this walk
    /// resolves, through the naming convention as well, because its column renders that way
    /// too. LIKE is left out as it is in <see cref="ScalarOfOther"/>: a pattern is a string
    /// whichever column it matches (decision 051).
    /// </summary>
    private ScalarType? TemporalScalarOf(QueryOperand? other, ComparisonOperator op, Dictionary<string, EntityMap> aliases)
    {
        if (op is ComparisonOperator.Like || other is null)
        {
            return null;
        }

        ScalarType? scalar;
        if (other.IsSubQuery)
        {
            scalar = ScalarOf(other.SubQuery!, aliases, RenderedEntityFor);
        }
        else if (other.IsColumn && !string.Equals(other.Function, "COUNT", StringComparison.OrdinalIgnoreCase))
        {
            scalar = ScalarOfColumn(aliases, other.Table, other.Property!);
        }
        else
        {
            return null;
        }

        return scalar is ScalarType.DateTime or ScalarType.Date or ScalarType.TimeOfDay ? scalar : null;
    }

    /// <summary>
    /// The constant in the scalar of its column and in the spelling the model carries - the
    /// one the LINQ reader produces and every visitor writes: a moment always with its time
    /// of day, because the JDBC escape the JPQL builder writes it into knows no shorter
    /// form, and a fraction of a second only when the source wrote one. Accepted are the
    /// extended ISO 8601 forms T-SQL and HQL both read unambiguously - a date, a date with
    /// a time of day after a space or a T, a time of day, each with an optional fraction -
    /// and nothing else: the value would fail in the database at run time, and here it
    /// fails with a record naming the column, or the subquery, and the scalar.
    /// </summary>
    private QueryConstant? Retype(QueryConstant constant, ScalarType scalar, QueryOperand other)
    {
        var spelled = scalar switch
        {
            ScalarType.DateTime => SpellMoment(constant.Text),
            ScalarType.Date => SpellDate(constant.Text),
            _ => SpellTimeOfDay(constant.Text),
        };

        if (spelled is null)
        {
            var expected = scalar switch
            {
                ScalarType.DateTime => "a date or a date with a time of day",
                ScalarType.Date => "a date",
                _ => "a time of day",
            };

            Report(
                ConversionRecordKind.Failure,
                $"The string '{constant.Text}' is compared with {(other.IsSubQuery ? "the value a subquery projects" : $"the column {other}")}, which is {scalar}, and does not read as {expected} in the ISO 8601 form; no artifact was generated.",
                QueryFeature.Filtering);
            return null;
        }

        return QueryConstant.Of(spelled, scalar);
    }

    private static readonly string[] MomentForms =
    [
        "yyyy-MM-dd",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
    ];

    private static readonly string[] TimeOfDayForms =
    [
        "HH:mm",
        "HH:mm:ss",
        "HH:mm:ss.FFFFFFF",
    ];

    private static string? SpellMoment(string text)
        => DateTime.TryParseExact(text, MomentForms, CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment)
            ? moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(moment.Ticks)
            : null;

    private static string? SpellDate(string text)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

    private static string? SpellTimeOfDay(string text)
        => TimeOnly.TryParseExact(text, TimeOfDayForms, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + Fraction(time.Ticks)
            : null;

    /// <summary>The fraction of a second when there is one, without the trailing zeros the source did not write.</summary>
    private static string Fraction(long ticks)
    {
        var rest = ticks % TimeSpan.TicksPerSecond;
        return rest == 0 ? string.Empty : "." + rest.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    private static string KeyOf(QueryParameter parameter)
        => parameter.Name ?? $"?{parameter.Position}";

    private static string Describe(QueryParameter parameter)
        => parameter.Name is { } name ? $"'{name}'" : $"at position {parameter.Position}";

    private static bool IsPlainIdentifier(string name)
        => (char.IsLetter(name[0]) || name[0] == '_')
           && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    /// <summary>
    /// One place the query names a parameter, with the scalar that place implies.
    /// <paramref name="IsRowCount"/> tells the two places apart: a condition operand takes
    /// its scalar from the other side of the comparison and yields to a scalar the source
    /// stated, whereas a row count takes it from the clause, which is not a guess and
    /// therefore does not yield (decision 085).
    /// </summary>
    private readonly record struct ParameterOccurrence(
        QueryParameter Parameter,
        ScalarType? Derived,
        bool IsRowCount = false);

    /// <summary>
    /// The parameters as C# declarations, each appended after the fixed first argument of
    /// the generated method (decision 083). A collection parameter is declared as the
    /// sequence it binds, which is what Dapper expands, what SetParameterList takes and what
    /// EF Core translates back into IN. The scalar goes in non-nullable: a comparison never
    /// tests NULL - that is its own operator (decision 002) - so a nullable property still
    /// yields a plain parameter.
    /// </summary>
    protected string CSharpParameters()
        => string.Concat(Parameters.Select(p =>
            $", {CSharpParameterType(p)} {QueryParameterNaming.IdentifierFor(p)}"));

    private static string CSharpParameterType(QueryParameter parameter)
    {
        var scalar = CSharpTypeConvertor.ToString(LangType.Scalar(parameter.Type!.Value));
        return parameter.IsCollection ? $"IEnumerable<{scalar}>" : scalar;
    }

    /// <summary>
    /// A row count as it is written where the target takes the value itself: the number, or
    /// the identifier the bound parameter is spelled with (decision 085). Three of the six
    /// targets put the count into an API call and need exactly this; a target that writes it
    /// into the query text wraps the same identifier in its own placeholder decoration.
    /// </summary>
    protected static string Spelled(RowCount count)
    {
        ArgumentNullException.ThrowIfNull(count);

        return count.IsParameter
            ? QueryParameterNaming.IdentifierFor(count.Parameter!)
            : count.Value!.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the condition tree holds a subquery operand anywhere (decision 061), inside an expression included.</summary>
    protected static bool ContainsSubQuery(ConditionNode? node) => node switch
    {
        null => false,
        ComparisonCondition comparison => ContainsSubQuery(comparison.Left) || ContainsSubQuery(comparison.Right),
        LogicalCondition logical => logical.Operands.Any(ContainsSubQuery),
        NotCondition negation => ContainsSubQuery(negation.Operand),
        _ => false,
    };

    private static bool ContainsSubQuery(QueryOperand? operand)
        => operand is not null
           && (operand.IsSubQuery
               || (operand.IsExpression
                   && (operand.Expression!.Leaves().Any(ContainsSubQuery)
                       || operand.Expression.Branches?.Any(b => ContainsSubQuery(b.When)) == true)));

    /// <summary>Whether the condition tree holds an expression operand anywhere (decision 107).</summary>
    protected static bool ContainsExpression(ConditionNode? node) => node switch
    {
        null => false,
        ComparisonCondition comparison => comparison.Left.IsExpression || comparison.Right?.IsExpression == true,
        LogicalCondition logical => logical.Operands.Any(ContainsExpression),
        NotCondition negation => ContainsExpression(negation.Operand),
        _ => false,
    };

    /// <summary>
    /// Normalizes the body of a subquery operand and applies the rules that hold for every
    /// target (decision 061), so that the three builders refuse the same shapes because one
    /// place refuses them: a body that is a set operation is not carried, and IN and scalar
    /// comparisons need a subquery projecting exactly one column - zero (the whole entity)
    /// or several is a query T-SQL itself rejects at run time, refused here earlier and with
    /// a record. Returns null when the operand cannot be rendered, having reported why.
    /// </summary>
    protected QueryClauses? NormalizeSubQueryOperand(SubQueryInstruction subQuery, ComparisonOperator op)
    {
        ArgumentNullException.ThrowIfNull(subQuery);

        var body = Unwrap(subQuery.Instructions);

        if (body.Count > 0 && body[0] is SetOperationInstruction && body.Skip(1).All(i => i is DistinctInstruction))
        {
            Report(
                ConversionRecordKind.Failure,
                "A set operation as the body of a subquery operand is not carried - the operand holds one SELECT; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
        }

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return null;
        }

        if (op is not ComparisonOperator.Exists && clauses.Projections.Count != 1)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The subquery projects {clauses.Projections.Count} columns where IN and scalar comparisons need exactly one; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
        }

        return clauses;
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
    /// eventually not (decision 009). What it reports is no longer a loss: a query emitted
    /// without the feature would return other rows (decision 053), so the target writes it
    /// in native SQL, or refuses where it has no API for that (decision 113).
    /// </summary>
    private void ReportUnspokenFeatures(QueryClauses clauses)
    {
        if (writesNativeSql)
        {
            return;
        }

        void Check(QueryFeature feature, bool present)
        {
            if (present && Descriptor.SupportOf(feature) == FactSupport.NotExpressible)
            {
                ReportUnspoken($"The query language of {Descriptor.Framework} cannot express {feature}", feature);
            }
        }

        Check(QueryFeature.Projection, clauses.Projections.Count > 0 || clauses.Distinct);
        Check(QueryFeature.Filtering, clauses.Filter is not null);
        Check(QueryFeature.Join, clauses.Joins.Count > 0);
        Check(QueryFeature.Aggregation, clauses.HasAggregates);
        Check(QueryFeature.Grouping, clauses.GroupBys.Count > 0);
        Check(QueryFeature.PostAggregationFiltering, clauses.PostFilter is not null);
        Check(QueryFeature.Ordering, clauses.OrderBys.Count > 0);
        Check(QueryFeature.Pagination, clauses.Offset is not null || clauses.Limit is not null);
        Check(QueryFeature.Subquery, ContainsSubQuery(clauses.Filter) || ContainsSubQuery(clauses.PostFilter));
        Check(
            QueryFeature.Expression,
            clauses.Projections.Any(p => p.Operand.IsExpression)
            || clauses.OrderBys.Any(o => o.Operand.IsExpression)
            || ContainsExpression(clauses.Filter)
            || ContainsExpression(clauses.PostFilter)
            || clauses.Joins.Any(j => ContainsExpression(j.OnCondition)));
    }

    /// <summary>
    /// Renders a set operation. The default says that the target's query language has none,
    /// which makes the target write the query in native SQL (decision 113); a target whose
    /// language has one overrides.
    /// </summary>
    protected virtual List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        ReportUnspoken(
            $"The query is a set operation ({instruction.OperationType}), and the query language of {Descriptor.Framework} has none",
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

    protected abstract void BuildPagination(QueryClauses clauses, QueryArtifact artifact);

    /// <summary>
    /// Joins the slots into the artifacts of the target framework. The count of artifacts is
    /// a property of the framework, which is why this returns a list (decision 025).
    /// </summary>
    protected abstract List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact);
}
