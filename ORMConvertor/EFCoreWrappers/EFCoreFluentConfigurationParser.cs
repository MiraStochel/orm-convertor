using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Reading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Reads EF Core's fluent configuration (decision 132): the body of OnModelCreating in a
/// context and the body of Configure in an IEntityTypeConfiguration&lt;T&gt;, wherever in the
/// conversion they stand. It stands first in the wrapper's list, before the class with its
/// annotations, because EF Core puts the fluent API above them (decision 068), and writes
/// through the builder's fill-only paths, so an annotation that says otherwise is a
/// conflict and the configuration stands.
///
/// What is read is a bounded syntactic subset, the part of the fluent API that is a
/// declaration rather than a program: a chain of calls over the builder of an entity whose
/// every argument is a literal, nameof, or a lambda naming a member. Whatever lies outside
/// - a loop, a condition, a variable as an argument, a call the subset does not know - is
/// a loss record at the statement or the call, never silence (decision 048). A call the
/// subset knows to return the same builder does not interrupt the chain; any other one
/// ends it, because what the next link stands on is no longer known.
///
/// A relationship names its navigations by lambda and not their type, and the target entity
/// is what the model needs, so it is left in the shared context for the class parser to
/// write when it reads the navigation; so are the table a DbSet property gives and the
/// default schema, which stand below the annotations.
/// </summary>
public sealed class EFCoreFluentConfigurationParser(
    AbstractEntityBuilder entityBuilder,
    EFCoreReadingContext context,
    SourceSqlDialect? declaredSourceDialect = null) : IEntityParser
{
    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType) => contentType == ConversionContentType.CSharp;

    /// <summary>How the unit being read was parsed (decision 128), for positions in the client's text.</summary>
    private CSharpReadResult unit;

    /// <summary>The entity maps this unit created or enriched (decision 066).</summary>
    private readonly List<EntityMap> touched = [];

    /// <summary>
    /// IsUnicode claims, written once the unit is read: EF Core lets an explicit store type
    /// decide and ignores the facet beside it, so HasColumnType goes first wherever in the
    /// configuration it stands - the order the class parser keeps for [Column] and [Unicode].
    /// </summary>
    private readonly List<(EntityMap Entity, string Property, bool IsUnicode, TextRange Range)> unicodeClaims = [];

    /// <summary>
    /// The configuration the unit holds, written into the builder; empty when it holds no
    /// context and no entity type configuration, or one that configures nothing.
    /// </summary>
    public IReadOnlyCollection<EntityMap> Parse(string source)
    {
        // The class parser, reading the same text after this one, is the one that reports a
        // text too deep to read or one that does not read without an error (decisions 092
        // and 128): one fact, one record. Here such a text is merely not read.
        if (NestingDepthGuard.FirstBeyond(Tracked(source), Limits) is not null)
        {
            return [];
        }

        var parsed = CSharpSource.Read(source);

        if (parsed.Reason is not null)
        {
            return [];
        }

        unit = parsed;
        touched.Clear();
        unicodeClaims.Clear();

        foreach (var cls in parsed.Tree!.GetCompilationUnitRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (EFCoreContext.IsContext(cls))
            {
                ReadContext(cls);
            }
            else if (EFCoreContext.ConfiguredEntity(cls) is { } configured)
            {
                ReadEntityTypeConfiguration(cls, configured);
            }
        }

        foreach (var (entity, property, isUnicode, range) in unicodeClaims)
        {
            entityBuilder.EntityMap = entity;
            using var scope = entityBuilder.At(range);
            entityBuilder.SetPropertyDatabaseType(property, type: null, isUnicode: isUnicode);
        }

        return [.. touched];
    }

    /* ---- the two places the configuration stands ---------------------------------- */

    private void ReadContext(ClassDeclarationSyntax cls)
    {
        // The DbSet properties: the table of each entity they expose, by EF Core's
        // convention, which the class parser materializes below the annotations.
        foreach (var dbSet in EFCoreContext.DbSetProperties(cls))
        {
            var type = dbSet.Type is NullableTypeSyntax nullable ? nullable.ElementType : dbSet.Type;

            if (GenericArgument(type) is not { } entityType)
            {
                continue;
            }

            var (entity, ns) = Named(entityType);

            // DbSet<T> makes T an entity type of the model, as Entity<T>() does.
            Entity(new EntityRef(entity, ns));

            if (context.AddDbSet(entity, dbSet.Identifier.Text) is { } first)
            {
                Report(ConversionRecordKind.Conflict, entity, null, MappingFactCategory.TableName,
                    $"The DbSet properties '{first}' and '{dbSet.Identifier.Text}' both expose the entity; "
                    + "the first one read gives its table (decisions 017 and 132).");
            }
        }

        foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text == "OnModelCreating" && m.ParameterList.Parameters.Count == 1))
        {
            var modelBuilder = method.ParameterList.Parameters[0].Identifier.Text;
            var where = $"OnModelCreating of '{cls.Identifier.Text}'";

            ReadBody(method.Body, method.ExpressionBody, where, new Scope(modelBuilder, null));
        }
    }

    private void ReadEntityTypeConfiguration(ClassDeclarationSyntax cls, TypeSyntax configured)
    {
        var (entity, ns) = Named(configured);
        Entity(new EntityRef(entity, ns));

        foreach (var method in cls.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.Text == "Configure" && m.ParameterList.Parameters.Count == 1))
        {
            var builder = method.ParameterList.Parameters[0].Identifier.Text;
            var where = $"Configure of '{cls.Identifier.Text}'";

            ReadBody(method.Body, method.ExpressionBody, where, new Scope(null, null).With(builder, new EntityRef(entity, ns)));
        }
    }

    /* ---- statements ----------------------------------------------------------------- */

    /// <summary>The model builder's name and the entity builders in reach, by name.</summary>
    private sealed record Scope(string? ModelBuilder, Dictionary<string, EntityRef>? Entities)
    {
        public Scope With(string name, EntityRef entity)
            => this with { Entities = new Dictionary<string, EntityRef>(Entities ?? new Dictionary<string, EntityRef>(), StringComparer.Ordinal) { [name] = entity } };

        public EntityRef? EntityOf(string name) => Entities?.GetValueOrDefault(name);
    }

    /// <summary>An entity as the configuration names it: the simple name, and the namespace a qualified name gives.</summary>
    private sealed record EntityRef(string Name, string? Namespace);

    private void ReadBody(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody, string where, Scope scope)
    {
        if (expressionBody is not null)
        {
            ReadExpressionStatement(expressionBody.Expression, expressionBody, where, scope);
            return;
        }

        if (body is null)
        {
            return;
        }

        foreach (var statement in body.Statements)
        {
            switch (statement)
            {
                case ExpressionStatementSyntax expressionStatement:
                    ReadExpressionStatement(expressionStatement.Expression, statement, where, scope);
                    break;

                // var b = modelBuilder.Entity<T>(); - the entity builder under a name of its own.
                case LocalDeclarationStatementSyntax
                {
                    Declaration.Variables: [{ Initializer.Value: InvocationExpressionSyntax initializer } variable]
                } when EntityOfModel(initializer, scope) is { } entity:
                    Entity(entity);
                    scope = scope.With(variable.Identifier.Text, entity);
                    break;

                default:
                    ReportStatement(statement, where);
                    break;
            }
        }
    }

    private void ReadExpressionStatement(ExpressionSyntax expression, SyntaxNode statement, string where, Scope scope)
    {
        if (expression is not InvocationExpressionSyntax || Flatten(expression) is not { } chain)
        {
            ReportStatement(statement, where);
            return;
        }

        // base.OnModelCreating(modelBuilder) states nothing.
        if (chain.Root is BaseExpressionSyntax)
        {
            return;
        }

        if (chain.Root is not IdentifierNameSyntax root)
        {
            ReportStatement(statement, where);
            return;
        }

        if (root.Identifier.Text == scope.ModelBuilder)
        {
            ReadModelChain(chain.Calls, scope);
        }
        else if (scope.EntityOf(root.Identifier.Text) is { } entity)
        {
            ReadEntityChain(entity, chain.Calls, 0);
        }
        else
        {
            ReportStatement(statement, where);
        }
    }

    /// <summary>modelBuilder.Entity&lt;T&gt;() exactly, as the initializer of a variable.</summary>
    private static EntityRef? EntityOfModel(InvocationExpressionSyntax invocation, Scope scope)
        => Flatten(invocation) is { Root: IdentifierNameSyntax root, Calls: [{ Name: "Entity", Arguments.Count: 0 } call] }
           && root.Identifier.Text == scope.ModelBuilder
           && call.TypeArgument is { } type
            ? ToEntity(type)
            : null;

    /* ---- chains --------------------------------------------------------------------- */

    /// <summary>One call of a chain: the method, its single type argument if any, its arguments and where it stands.</summary>
    private sealed record Call(string Name, TypeSyntax? TypeArgument, SeparatedSyntaxList<ArgumentSyntax> Arguments, TextSpan Span);

    private sealed record Chain(ExpressionSyntax Root, List<Call> Calls);

    /// <summary>
    /// a.B(x).C&lt;T&gt;(y) as its root a and the calls in order; null where a link is not a call
    /// on a member - an indexer, a call through a delegate -, which is no chain the subset reads.
    /// </summary>
    private static Chain? Flatten(ExpressionSyntax expression)
    {
        var calls = new List<Call>();

        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation)
        {
            calls.Add(new Call(
                member.Name.Identifier.Text,
                member.Name is GenericNameSyntax { TypeArgumentList.Arguments: [var typeArgument] } ? typeArgument : null,
                invocation.ArgumentList.Arguments,
                TextSpan.FromBounds(member.Name.SpanStart, invocation.ArgumentList.Span.End)));

            expression = member.Expression;
        }

        if (calls.Count == 0 || expression is InvocationExpressionSyntax)
        {
            return null;
        }

        calls.Reverse();
        return new Chain(expression, calls);
    }

    private void ReadModelChain(List<Call> calls, Scope scope)
    {
        for (var i = 0; i < calls.Count; i++)
        {
            var call = calls[i];

            switch (call.Name)
            {
                case "Entity" when call.TypeArgument is { } type && call.Arguments.Count == 0:
                    // Entity<T>() hands over the entity's builder, and the rest of the chain is its.
                    Entity(ToEntity(type));
                    ReadEntityChain(ToEntity(type), calls, i + 1);
                    return;

                case "Entity" when call.TypeArgument is { } type
                    && call.Arguments is [{ Expression: SimpleLambdaExpressionSyntax lambda }]:
                    // Entity<T>(b => …) configures the entity inside the lambda and hands the
                    // model builder back.
                    Entity(ToEntity(type));
                    ReadLambda(lambda, ToEntity(type));
                    break;

                case "HasDefaultSchema":
                    if (Literal(call, 0) is { } schema)
                    {
                        if (context.SetDefaultSchema(schema) is { } first)
                        {
                            Report(ConversionRecordKind.Conflict, null, null, MappingFactCategory.SchemaName,
                                $"HasDefaultSchema states '{first}' in one configuration and '{schema}' in another; "
                                + "the first one read stands (decisions 017 and 132).");
                        }
                    }
                    else
                    {
                        ReportNotLiteral(call, null, null);
                    }

                    break;

                // The registration of a configuration class states nothing: the class is read
                // where it stands (decision 132).
                case "ApplyConfiguration":
                case "ApplyConfigurationsFromAssembly":
                    break;

                default:
                    ReportEndOfChain(calls, i, null, null);
                    return;
            }
        }
    }

    private void ReadLambda(SimpleLambdaExpressionSyntax lambda, EntityRef entity)
    {
        var scope = new Scope(null, null).With(lambda.Parameter.Identifier.Text, entity);
        var where = $"the configuration of '{entity.Name}'";

        if (lambda.Block is { } block)
        {
            ReadBody(block, null, where, scope);
        }
        else if (lambda.ExpressionBody is { } body)
        {
            ReadExpressionStatement(body, body, where, scope);
        }
    }

    /// <summary>The calls of EntityTypeBuilder that return it unchanged and state nothing the model carries.</summary>
    private static readonly HashSet<string> UnreadEntityCalls =
    [
        "HasComment", "HasAnnotation", "HasCheckConstraint", "HasData", "HasQueryFilter", "HasChangeTrackingStrategy",
        "UsePropertyAccessMode", "ToView", "ToSqlQuery", "ToFunction", "IsMemoryOptimized", "HasBaseType",
        "UseTphMappingStrategy", "UseTptMappingStrategy", "UseTpcMappingStrategy", "Metadata",
    ];

    /// <summary>The calls of PropertyBuilder that return it unchanged and state nothing the model carries.</summary>
    private static readonly HashSet<string> UnreadPropertyCalls =
    [
        "HasComment", "HasAnnotation", "HasDefaultValue", "HasDefaultValueSql", "HasComputedColumnSql", "HasConversion",
        "IsFixedLength", "UseCollation", "HasField", "UsePropertyAccessMode", "ValueGeneratedOnAddOrUpdate",
        "ValueGeneratedOnUpdate", "ValueGeneratedOnUpdateSometimes", "HasSentinel", "IsSparse", "UseIdentityColumn",
        "UseHiLo", "UseSequence", "HasValueGenerator", "HasColumnOrder",
    ];

    /// <summary>The calls of a relationship builder that return it unchanged and state nothing the model carries.</summary>
    private static readonly HashSet<string> UnreadRelationshipCalls =
    [
        "HasPrincipalKey", "IsRequired", "OnDelete", "HasConstraintName", "HasAnnotation",
    ];

    /// <summary>The calls of an index builder that return it unchanged and state nothing the model carries.</summary>
    private static readonly HashSet<string> UnreadIndexCalls =
    [
        "IsDescending", "IncludeProperties", "IsClustered", "HasFillFactor", "IsCreatedOnline", "SortInTempDb",
        "UseDataCompression", "HasAnnotation",
    ];

    private void ReadEntityChain(EntityRef entity, List<Call> calls, int from)
    {
        for (var i = from; i < calls.Count; i++)
        {
            var call = calls[i];

            switch (call.Name)
            {
                case "ToTable":
                    ReadToTable(entity, call);
                    break;

                case "HasKey":
                    if (Members(call.Arguments) is not { Count: > 0 } keyParts)
                    {
                        ReportNotLiteral(call, entity.Name, null);
                        return;
                    }

                    Write(entity, call, () => entityBuilder.AddPrimaryKey(
                        keyParts.Select((name, index) => (name, index + 1, PrimaryKeyStrategy.Unspecified)).ToList()));

                    // KeyBuilder: the name of the primary key constraint has no place in the model.
                    ReadRest(calls, i + 1, entity.Name, null, ["HasName", "HasAnnotation"]);
                    return;

                case "HasNoKey" when call.Arguments.Count == 0:
                    Write(entity, call, entityBuilder.MarkNoKey);
                    break;

                case "HasAlternateKey":
                case "HasIndex":
                    ReadKeyOrIndex(entity, calls, i);
                    return;

                case "Ignore":
                    if (Members(call.Arguments) is [var ignored])
                    {
                        Write(entity, call, () => entityBuilder.MarkTransient(ignored));
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, null);
                    }

                    break;

                case "Property" when call.Arguments is [{ Expression: LambdaExpressionSyntax }]:
                    if (Members(call.Arguments) is [var property])
                    {
                        ReadPropertyChain(entity, property, calls, i + 1);
                    }
                    else
                    {
                        ReportEndOfChain(calls, i, entity.Name, null);
                    }

                    return;

                case "Property":
                    // A property named by a string may be a shadow property - a column with no
                    // member -, which the model has no place for.
                    Report(ConversionRecordKind.Loss, entity.Name, Literal(call, 0), null,
                        $"The call {Spelled(call)} names the property by a string, which in EF Core may be a shadow property - "
                        + "a column no member of the class carries; the model has no place for one, so the call and the rest "
                        + $"of its chain{Rest(calls, i)} were dropped (decision 132).");
                    return;

                case "HasOne":
                case "HasMany":
                    ReadRelationshipChain(entity, calls, i);
                    return;

                default:
                    if (UnreadEntityCalls.Contains(call.Name))
                    {
                        ReportUnread(call, entity.Name, null);
                        break;
                    }

                    ReportEndOfChain(calls, i, entity.Name, null);
                    return;
            }
        }
    }

    private void ReadToTable(EntityRef entity, Call call)
    {
        string? table = null;
        string? schema = null;
        var unread = false;

        for (var a = 0; a < call.Arguments.Count; a++)
        {
            var argument = call.Arguments[a];
            var named = argument.NameColon?.Name.Identifier.Text;

            if (argument.Expression is LambdaExpressionSyntax)
            {
                unread = true; // the table builder: check constraints, triggers, column overrides
            }
            else if (named == "schema" || (named is null && a == 1))
            {
                schema = LiteralString(argument.Expression);
                unread |= schema is null;
            }
            else if (named is null or "name")
            {
                table = LiteralString(argument.Expression);
                unread |= table is null && argument.Expression is not LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression };
            }
        }

        if (table is not null || schema is not null)
        {
            Write(entity, call, () =>
            {
                if (table is not null)
                {
                    entityBuilder.AddTable(table);
                }

                if (schema is not null)
                {
                    entityBuilder.AddSchema(schema);
                }
            });
        }

        if (unread)
        {
            Report(ConversionRecordKind.Loss, entity.Name, null, MappingFactCategory.TableName,
                $"The call {Spelled(call)} has an argument that is neither a literal nor nameof - or a table builder, whose "
                + "contents the model has no place for -, and what that argument states was dropped (decision 132).");
        }
    }

    /// <summary>
    /// HasAlternateKey and HasIndex: both end in a unique constraint (decision 055) - an
    /// alternate key is one in the database -, and an index that is not unique, or that
    /// HasFilter narrows to part of the table, is not a constraint over the table and is a
    /// loss, as [Index] without IsUnique is.
    /// </summary>
    private void ReadKeyOrIndex(EntityRef entity, List<Call> calls, int at)
    {
        var call = calls[at];
        var isKey = call.Name == "HasAlternateKey";

        // HasIndex(e => e.A, "IX_Name") names the index beside the members.
        var memberArguments = call.Arguments;
        string? name = null;

        if (!isKey && memberArguments.Count == 2 && memberArguments[0].Expression is LambdaExpressionSyntax)
        {
            name = LiteralString(memberArguments[1].Expression);
            memberArguments = memberArguments.RemoveAt(1);
        }

        if (Members(memberArguments) is not { Count: > 0 } members)
        {
            ReportNotLiteral(call, entity.Name, null);
            return;
        }

        var unique = isKey;
        var filtered = false;

        for (var i = at + 1; i < calls.Count; i++)
        {
            var next = calls[i];

            switch (next.Name)
            {
                case "IsUnique" when !isKey:
                    unique = next.Arguments.Count == 0 || LiteralBool(next.Arguments[0].Expression) == true;
                    break;

                case "HasName":
                case "HasDatabaseName" when !isKey:
                    name = Literal(next, 0) ?? name;
                    break;

                case "HasFilter" when !isKey:
                    filtered = true;
                    Report(ConversionRecordKind.Loss, entity.Name, null, MappingFactCategory.UniqueConstraint,
                        $"The index over ({string.Join(", ", members)}) is narrowed by {Spelled(next)} to part of the table; "
                        + "it is no constraint over the table, which is the only kind the model carries, and was dropped (decisions 055 and 132).");
                    break;

                default:
                    if (!isKey && UnreadIndexCalls.Contains(next.Name))
                    {
                        ReportUnread(next, entity.Name, null);
                        break;
                    }

                    ReportEndOfChain(calls, i, entity.Name, null);
                    i = calls.Count;
                    break;
            }
        }

        if (filtered)
        {
            return;
        }

        if (!unique)
        {
            Report(ConversionRecordKind.Loss, entity.Name, null, null,
                $"The index over ({string.Join(", ", members)}) is not unique, so it states no "
                + "mapping fact the intermediate representation carries, and was dropped (decision 055).");
            return;
        }

        Write(entity, call, () => entityBuilder.AddUniqueConstraint(name, members));
    }

    private void ReadPropertyChain(EntityRef entity, string property, List<Call> calls, int from)
    {
        for (var i = from; i < calls.Count; i++)
        {
            var call = calls[i];

            switch (call.Name)
            {
                case "HasColumnName":
                    WriteColumnFact(entity, property, call, "ColumnName", Literal(call, 0), MappingFactCategory.ColumnName);
                    break;

                case "HasMaxLength":
                    WriteColumnFact(entity, property, call, "Length", LiteralNumber(call, 0), MappingFactCategory.Length);
                    break;

                case "HasPrecision":
                    if (LiteralNumber(call, 0) is { } precision)
                    {
                        var facts = new Dictionary<string, string> { ["Precision"] = precision };

                        if (call.Arguments.Count > 1 && LiteralNumber(call, 1) is { } scale)
                        {
                            facts["Scale"] = scale;
                        }

                        Write(entity, call, () => entityBuilder.SetPropertyDatabaseMapping(property, facts));
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, property);
                    }

                    break;

                case "IsRequired":
                    if (Flag(call) is { } required)
                    {
                        Write(entity, call, () => entityBuilder.SetPropertyDatabaseMapping(
                            property, new Dictionary<string, string> { ["Nullable"] = required ? "false" : "true" }));
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, property);
                    }

                    break;

                case "IsUnicode":
                    if (Flag(call) is { } isUnicode)
                    {
                        unicodeClaims.Add((Entity(entity), property, isUnicode, Range(call.Span)));
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, property);
                    }

                    break;

                case "HasColumnType":
                    if (Literal(call, 0) is { } typeName)
                    {
                        Write(entity, call, () => EFCoreStoreType.Apply(
                            entityBuilder, property, typeName, declaredSourceDialect, Spelled(call)));
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, property);
                    }

                    break;

                case "IsRowVersion" when call.Arguments.Count == 0:
                    context.MarkRowVersion(entity.Name, property);
                    Write(entity, call, () => entityBuilder.SetPropertyDatabaseMapping(
                        property, new Dictionary<string, string> { ["IsVersion"] = "true" }));
                    break;

                case "IsConcurrencyToken":
                    // Read by the class parser under the conditions of decision 116, which
                    // only the class can answer.
                    if (Flag(call) is { } token)
                    {
                        if (token)
                        {
                            context.MarkConcurrencyToken(entity.Name, property);
                        }
                    }
                    else
                    {
                        ReportNotLiteral(call, entity.Name, property);
                    }

                    break;

                case "ValueGeneratedNever" when call.Arguments.Count == 0:
                    context.SetGeneratedOption(entity.Name, property, "None");
                    break;

                case "ValueGeneratedOnAdd" when call.Arguments.Count == 0:
                    context.SetGeneratedOption(entity.Name, property, "Identity");
                    break;

                default:
                    if (UnreadPropertyCalls.Contains(call.Name))
                    {
                        ReportUnread(call, entity.Name, property);
                        break;
                    }

                    ReportEndOfChain(calls, i, entity.Name, property);
                    return;
            }
        }
    }

    private void WriteColumnFact(EntityRef entity, string property, Call call, string key, string? value, MappingFactCategory category)
    {
        if (value is null)
        {
            ReportNotLiteral(call, entity.Name, property);
            return;
        }

        Write(entity, call, () => entityBuilder.SetPropertyDatabaseMapping(property, new Dictionary<string, string> { [key] = value }));
    }

    /// <summary>
    /// HasOne or HasMany, WithOne or WithMany, and HasForeignKey: the relationship as the
    /// configured side states it, left for the class parser to write (decision 132). A
    /// navigation named by anything but a lambda or a string - HasOne&lt;T&gt;() alone, a
    /// relationship without a navigation on this side - has nothing in the model to stand on.
    /// </summary>
    private void ReadRelationshipChain(EntityRef entity, List<Call> calls, int at)
    {
        var call = calls[at];
        var navigationIsCollection = call.Name == "HasMany";

        if (Members(call.Arguments) is not [var navigation])
        {
            Report(ConversionRecordKind.Loss, entity.Name, null, MappingFactCategory.ForeignKeyColumns,
                $"The call {Spelled(call)} configures a relationship without a navigation on '{entity.Name}'; the model "
                + $"carries a relationship on its navigation only, so the call and the rest of its chain{Rest(calls, at)} "
                + "were dropped (decision 132).");
            return;
        }

        if (at + 1 >= calls.Count || calls[at + 1] is not { Name: "WithOne" or "WithMany" } with)
        {
            // HasOne without WithOne or WithMany leaves the far end to EF Core's convention,
            // which is what the class parser applies to the navigation anyway.
            if (at + 1 < calls.Count)
            {
                ReportEndOfChain(calls, at + 1, entity.Name, navigation);
            }

            return;
        }

        var inverseIsCollection = with.Name == "WithMany";
        string? inverse = null;

        if (with.Arguments.Count > 0)
        {
            if (Members(with.Arguments) is [var named])
            {
                inverse = named;
            }
            else
            {
                ReportEndOfChain(calls, at + 1, entity.Name, navigation);
                return;
            }
        }

        IReadOnlyList<string>? foreignKey = null;
        string? dependent = null;

        for (var i = at + 2; i < calls.Count; i++)
        {
            var next = calls[i];

            if (next.Name == "HasForeignKey" && Members(next.Arguments) is { Count: > 0 } keyProperties)
            {
                foreignKey = keyProperties;
                dependent = next.TypeArgument is { } dependentType ? ToEntity(dependentType).Name : null;
                continue;
            }

            if (next.Name == "UsingEntity")
            {
                Report(ConversionRecordKind.Loss, entity.Name, navigation, MappingFactCategory.ForeignKeyColumns,
                    $"The call {Spelled(next)} names the junction of the many-to-many; it and the rest of its chain"
                    + $"{Rest(calls, i)} were not read, and the junction is left to the database catalog (decisions 005 and 132).");
                break;
            }

            if (UnreadRelationshipCalls.Contains(next.Name))
            {
                ReportUnread(next, entity.Name, navigation);
                continue;
            }

            ReportEndOfChain(calls, i, entity.Name, navigation);
            break;
        }

        if (!navigationIsCollection && !inverseIsCollection && dependent is null)
        {
            // A one-to-one names the entity that holds the key only through HasForeignKey<T>;
            // without it neither side can be told owning, and the navigation is left to the
            // convention the class parser applies.
            Report(ConversionRecordKind.Incompleteness, entity.Name, navigation, MappingFactCategory.ForeignKeyColumns,
                $"The one-to-one over '{navigation}' names no dependent with HasForeignKey<T>, so the configuration does not "
                + "say which side holds the foreign key; the navigation is read by EF Core's convention instead (decision 132).");
            return;
        }

        context.AddRelationship(new FluentRelationship(
            entity.Name, navigation, navigationIsCollection, inverse, inverseIsCollection,
            navigationIsCollection && inverseIsCollection ? null : foreignKey,
            dependent));
    }

    /// <summary>
    /// After a call whose builder the subset reads only a few calls of - the key builder -:
    /// the named ones are dropped one by one, anything else ends the chain.
    /// </summary>
    private void ReadRest(List<Call> calls, int from, string entity, string? property, string[] unreadSameBuilder)
    {
        for (var i = from; i < calls.Count; i++)
        {
            if (unreadSameBuilder.Contains(calls[i].Name))
            {
                ReportUnread(calls[i], entity, property);
                continue;
            }

            ReportEndOfChain(calls, i, entity, property);
            return;
        }
    }

    /* ---- writing ---------------------------------------------------------------------- */

    /// <summary>
    /// The entity the configuration names, found among those of the conversion or founded
    /// here and made current - the lookup of a mapping artifact, which says nothing about a
    /// namespace unless the name is qualified (decision 094, as orm.xml does it).
    /// </summary>
    private EntityMap Entity(EntityRef entity)
    {
        var existing = entityBuilder.EntityMaps.FirstOrDefault(em =>
                string.Equals(em.Entity.Name, entity.Name, StringComparison.Ordinal)
                && entity.Namespace is not null
                && string.Equals(em.Entity.Namespace, entity.Namespace, StringComparison.Ordinal))
            ?? entityBuilder.EntityMaps.FirstOrDefault(em =>
                string.Equals(em.Entity.Name, entity.Name, StringComparison.Ordinal));

        if (existing is null)
        {
            entityBuilder.BeginEntity();
            entityBuilder.AddClassHeader(string.Empty, entity.Name);

            if (entity.Namespace is not null)
            {
                entityBuilder.AddNamespace(entity.Namespace);
            }
        }
        else
        {
            entityBuilder.EntityMap = existing;
        }

        // The class is what carries the language facts; without one in the conversion the
        // entity is refused rather than generated empty.
        entityBuilder.MarkNamedByMapping();

        if (!touched.Contains(entityBuilder.EntityMap))
        {
            touched.Add(entityBuilder.EntityMap);
        }

        return entityBuilder.EntityMap;
    }

    /// <summary>Writes one fact of the entity, placed at the call that states it (decision 125).</summary>
    private void Write(EntityRef entity, Call call, Action write)
    {
        Entity(entity);
        using var scope = entityBuilder.At(Range(call.Span));
        write();
    }

    private TextRange Range(TextSpan span) => new(unit.ToClient(span.Start), span.Length);

    /* ---- reading arguments --------------------------------------------------------- */

    private static EntityRef ToEntity(TypeSyntax type)
    {
        var (name, ns) = Named(type);
        return new EntityRef(name, ns);
    }

    /// <summary>The simple name of a written type and the namespace a qualified spelling gives.</summary>
    private static (string Name, string? Namespace) Named(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => (qualified.Right.Identifier.Text, qualified.Left.ToString()),
        AliasQualifiedNameSyntax alias => (alias.Name.Identifier.Text, null),
        SimpleNameSyntax simple => (simple.Identifier.Text, null),
        _ => (type.ToString(), null),
    };

    private static TypeSyntax? GenericArgument(TypeSyntax type) => type switch
    {
        GenericNameSyntax { TypeArgumentList.Arguments: [var argument] } => argument,
        QualifiedNameSyntax { Right: GenericNameSyntax { TypeArgumentList.Arguments: [var argument] } } => argument,
        AliasQualifiedNameSyntax { Name: GenericNameSyntax { TypeArgumentList.Arguments: [var argument] } } => argument,
        _ => null,
    };

    /// <summary>
    /// The members a call names, in their order: one lambda e =&gt; e.A or e =&gt; new { e.A, e.B },
    /// or strings and nameof - the params string[] overloads of HasKey, HasIndex,
    /// HasForeignKey. Null where any argument is something else.
    /// </summary>
    private static List<string>? Members(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        if (arguments is [{ Expression: LambdaExpressionSyntax lambda }])
        {
            var parameter = lambda switch
            {
                SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
                ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters: [var single] } => single.Identifier.Text,
                _ => null,
            };

            return lambda.ExpressionBody switch
            {
                MemberAccessExpressionSyntax member when IsParameter(member, parameter) => [member.Name.Identifier.Text],
                AnonymousObjectCreationExpressionSyntax anonymous
                    when anonymous.Initializers.All(i => i.NameEquals is null && i.Expression is MemberAccessExpressionSyntax m && IsParameter(m, parameter))
                    => [.. anonymous.Initializers.Select(i => ((MemberAccessExpressionSyntax)i.Expression).Name.Identifier.Text)],
                _ => null,
            };
        }

        var names = new List<string>();

        foreach (var argument in arguments)
        {
            if (LiteralString(argument.Expression) is not { } name)
            {
                return null;
            }

            names.Add(name);
        }

        return names.Count == 0 ? null : names;
    }

    private static bool IsParameter(MemberAccessExpressionSyntax member, string? parameter)
        => member.Expression is IdentifierNameSyntax identifier && identifier.Identifier.Text == parameter;

    private static string? Literal(Call call, int index)
        => call.Arguments.Count > index ? LiteralString(call.Arguments[index].Expression) : null;

    private static string? LiteralNumber(Call call, int index)
        => call.Arguments.Count > index
           && call.Arguments[index].Expression is LiteralExpressionSyntax { Token.Value: int value }
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>A string literal or nameof(X); null for anything else.</summary>
    private static string? LiteralString(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax { Token.Value: string value } => value,
        InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
            ArgumentList.Arguments: [{ Expression: var named }],
        } => named switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => null,
        },
        _ => null,
    };

    private static bool? LiteralBool(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax { Token.Value: bool value } ? value : null;

    /// <summary>The flag of IsRequired(), IsUnicode(): true without an argument, the literal with one.</summary>
    private static bool? Flag(Call call)
        => call.Arguments.Count == 0 ? true : LiteralBool(call.Arguments[0].Expression);

    /* ---- records --------------------------------------------------------------------- */

    private void Report(ConversionRecordKind kind, string? entity, string? property, MappingFactCategory? category, string reason)
        => entityBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = ORMEnum.EFCore,
            Artifact = ConversionContentType.CSharp,
            Entity = entity,
            Property = property,
            Category = category,
            Reason = reason,
        });

    private void ReportStatement(SyntaxNode statement, string where)
        => Report(ConversionRecordKind.Loss, null, null, null,
            $"The statement '{Excerpt(statement)}' in {where} is outside the fluent configuration the tool reads - a chain "
            + "of calls over the builder of an entity, with literal arguments - and was dropped (decision 132).");

    private void ReportUnread(Call call, string? entity, string? property)
        => Report(ConversionRecordKind.Loss, entity, property, null,
            $"The call {Spelled(call)} has no counterpart in the intermediate representation and was dropped (decision 132).");

    private void ReportNotLiteral(Call call, string? entity, string? property)
        => Report(ConversionRecordKind.Loss, entity, property, null,
            $"The call {Spelled(call)} has an argument that is neither a literal, nameof nor a lambda naming a member, "
            + "so what it states is not read and was dropped (decision 132).");

    private void ReportEndOfChain(List<Call> calls, int at, string? entity, string? property)
        => Report(ConversionRecordKind.Loss, entity, property, null,
            $"The call {Spelled(calls[at])} is outside the fluent configuration the tool reads; it and the rest of its "
            + $"chain{Rest(calls, at)} were dropped (decision 132).");

    private static string Spelled(Call call) => $"{call.Name}({(call.Arguments.Count == 0 ? string.Empty : "…")})";

    private static string Rest(List<Call> calls, int at)
        => at + 1 < calls.Count ? $" ({string.Join(", ", calls.Skip(at + 1).Select(Spelled))})" : string.Empty;

    private static string Excerpt(SyntaxNode node)
    {
        var text = string.Join(" ", node.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        return text.Length <= 80 ? text : text[..77] + "...";
    }

    /// <summary>The C# text as the shared nesting guard reads it (decision 092), as the class parser reads it.</summary>
    private static IEnumerable<SourceToken> Tracked(string source)
    {
        var text = SourceText.From(source);

        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            var position = text.Lines.GetLinePosition(token.SpanStart);
            yield return new SourceToken(token.Text, position.Line + 1, position.Character + 1);
        }
    }
}
