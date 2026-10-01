using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LinqParsing;

/// <summary>
/// The variables of a unit that hold a LINQ query, and what the code does with them
/// (decision 109). An <c>IQueryable</c> is not run where it is written but when it is
/// enumerated, and <c>rich.Select(f)</c> is <c>Queryable.Select(rich, f)</c>, a tree that
/// holds the whole tree of <c>rich</c>: code that keeps a query in a variable and continues it
/// in another statement hands the provider one query, composed of both. So a chain whose head
/// is such a variable is read on from the chain the variable holds, as if it were written in
/// one expression - where the text fixes that chain: the variable is assigned once, and what it
/// holds is a query not yet run.
///
/// Which declaration a name means is decided by the scoping rules of C#, not by the name, so
/// the variables are bound through Roslyn's semantic model over the unit alone, with no
/// references: binding a local to its declaration needs no types, there is no framework in it
/// (S1), and it comes out the same on every run (S2). The model is built only when a chain
/// leans on a name the unit itself assigns a value to, which a query that uses no variable of
/// the unit never does.
/// </summary>
internal sealed class QueryVariables
{
    /// <summary>
    /// The steps that run a query rather than add to it: materialization, a single row, an
    /// aggregate, a bulk operation. After one of them the variable holds rows or a value, and
    /// what the code does with it is C# over objects in memory, not a query.
    /// </summary>
    private static readonly HashSet<string> Executing = new(StringComparer.Ordinal)
    {
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "ToLookup", "AsEnumerable", "AsAsyncEnumerable",
        "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault", "ElementAt", "ElementAtOrDefault",
        "Count", "LongCount", "Sum", "Average", "Min", "Max", "MinBy", "MaxBy", "Aggregate", "Any", "All", "Contains",
        "Load", "ForEach", "ExecuteDelete", "ExecuteUpdate",
    };

    private readonly SyntaxNode root;
    private readonly Func<ExpressionSyntax, bool> isQuery;

    /// <summary>Names the unit declares with a value or assigns to; nothing else can hold a query.</summary>
    private readonly HashSet<string> assigned;

    /// <summary>Names that stand at the head of a call, <c>name.Step(…)</c>; nothing else continues a query.</summary>
    private readonly HashSet<string> continued;

    private readonly Dictionary<ISymbol, Variable> described = new(SymbolEqualityComparer.Default);
    private readonly HashSet<ISymbol> following = new(SymbolEqualityComparer.Default);
    private SemanticModel? model;

    /// <param name="root">The unit, after the rewrite of its query expressions (decision 103).</param>
    /// <param name="isQuery">Whether an expression decomposes to a query root, which the parser knows.</param>
    public QueryVariables(SyntaxNode root, Func<ExpressionSyntax, bool> isQuery)
    {
        this.root = root;
        this.isQuery = isQuery;

        assigned =
        [
            .. root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(declarator => declarator.Initializer is not null)
                .Select(declarator => declarator.Identifier.Text),
            .. root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
                .Select(assignment => assignment.Left)
                .OfType<IdentifierNameSyntax>()
                .Select(identifier => identifier.Identifier.Text),
        ];

        continued =
        [
            .. root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(identifier => IsContinuation(identifier) || IsJoinedSequence(identifier))
                .Select(identifier => identifier.Identifier.Text),
        ];
    }

    /// <summary>How a chain's value is kept, as far as it decides whether the chain is a query of its own.</summary>
    public enum StorageKind
    {
        /// <summary>Not kept in a variable the code continues: the chain is a query of its own.</summary>
        None,

        /// <summary>Kept in a variable assigned once and only ever continued: the chain is part of the queries that continue it.</summary>
        Prefix,

        /// <summary>Kept in a variable the code continues, whose value the text does not fix: a query composed at run time.</summary>
        RunTime,
    }

    /// <summary>
    /// The chain the variable at the head of a chain holds, when the text fixes it: a variable
    /// assigned once, whose value is a query not yet run. Casts and parentheses around the
    /// value are no part of the query and are left behind.
    /// </summary>
    public bool TryFollow(IdentifierNameSyntax head, out ExpressionSyntax? held)
    {
        held = null;

        if (!assigned.Contains(head.Identifier.Text) || SymbolOf(head) is not { } symbol)
        {
            return false;
        }

        held = FollowableValue(symbol);
        return held is not null;
    }

    /// <summary>
    /// Whether a chain that decomposes to a query root is a query of its own (decision 109). It
    /// is not when the code keeps it in a variable assigned once and does nothing with the
    /// variable but continue it: the queries are the continuations, and the chain is their
    /// beginning. It is refused when the code continues a variable whose value is assigned more
    /// than once or chosen by a condition, since which query reaches the provider is then
    /// decided at run time. In every other case - kept nowhere, run already, returned,
    /// enumerated, passed on - it is a query of its own.
    /// </summary>
    public (StorageKind Kind, string? Variable) StorageOf(ExpressionSyntax chain)
    {
        if (IsExecuted(chain))
        {
            return (StorageKind.None, null);
        }

        SyntaxNode node = chain;
        var direct = true;

        while (true)
        {
            switch (node.Parent)
            {
                case ParenthesizedExpressionSyntax or CastExpressionSyntax:
                case BinaryExpressionSyntax asExpression when asExpression.IsKind(SyntaxKind.AsExpression) && asExpression.Left == node:
                    node = node.Parent;
                    continue;

                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                case ConditionalExpressionSyntax conditional when conditional.Condition != node:
                    direct = false;
                    node = node.Parent;
                    continue;
            }

            break;
        }

        var name = node.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => declarator.Identifier.Text,
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } assignment
                when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == node => left.Identifier.Text,
            _ => null,
        };

        // Before the model is built: a name nothing continues keeps no query that goes on.
        if (name is null || !continued.Contains(name))
        {
            return (StorageKind.None, null);
        }

        var symbol = node.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => SymbolOf(declarator),
            AssignmentExpressionSyntax { Left: IdentifierNameSyntax left } => SymbolOf(left),
            _ => null,
        };

        if (symbol is null)
        {
            return (StorageKind.None, null);
        }

        var variable = Describe(symbol);
        var continuations = variable.Reads.Count(read => IsContinuation(read) || IsJoinedSequence(read));

        if (continuations == 0)
        {
            return (StorageKind.None, null);
        }

        if (direct && FollowableValue(symbol) is not null)
        {
            return continuations == variable.Reads.Count ? (StorageKind.Prefix, name) : (StorageKind.None, null);
        }

        return (StorageKind.RunTime, name);
    }

    /// <summary>Whether the expression runs its query: it ends in a step that does, or it is awaited.</summary>
    public static bool IsExecuted(ExpressionSyntax value) => Stripped(value) switch
    {
        AwaitExpressionSyntax => true,
        InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } =>
            Executing.Contains(WithoutAsync(member.Name.Identifier.Text)),
        _ => false,
    };

    /// <summary>The identifier stands at the head of a call: <c>name.Step(…)</c>.</summary>
    private static bool IsContinuation(IdentifierNameSyntax identifier)
        => identifier.Parent is MemberAccessExpressionSyntax member
           && member.Expression == identifier
           && member.Parent is InvocationExpressionSyntax invocation
           && invocation.Expression == member;

    /// <summary>
    /// The identifier is the inner sequence of a join: <c>q.Join(name, …)</c>. The query it
    /// holds is read into the query that joins it, as an intermediate result named after the
    /// variable (decision 112), so a variable read only so - or so and by continuation - holds
    /// the beginning of that query, not a query of its own.
    /// </summary>
    private static bool IsJoinedSequence(IdentifierNameSyntax identifier)
        => identifier.Parent is ArgumentSyntax argument
           && argument.Parent is ArgumentListSyntax list
           && list.Arguments.IndexOf(argument) == 0
           && list.Parent is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Join" or "LeftJoin" or "RightJoin" } };

    private static string WithoutAsync(string method)
        => method.Length > 5 && method.EndsWith("Async", StringComparison.Ordinal) ? method[..^5] : method;

    private static ExpressionSyntax Stripped(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case BinaryExpressionSyntax asExpression when asExpression.IsKind(SyntaxKind.AsExpression):
                    expression = asExpression.Left;
                    continue;
                default:
                    return expression;
            }
        }
    }

    /// <summary>
    /// The value of a variable assigned once, when that value is a query not yet run; null
    /// otherwise. A variable met again while its own value is being followed is a cycle the
    /// compiler would reject, and it follows nothing.
    /// </summary>
    private ExpressionSyntax? FollowableValue(ISymbol symbol)
    {
        var variable = Describe(symbol);

        if (variable.Writes.Count != 1 || variable.Writes[0] is not { } written)
        {
            return null;
        }

        var value = Stripped(written);

        if (IsExecuted(value) || !following.Add(symbol))
        {
            return null;
        }

        try
        {
            return isQuery(value) ? value : null;
        }
        finally
        {
            following.Remove(symbol);
        }
    }

    /// <summary>
    /// Every write and every read of a variable in the unit. A write whose value the text does
    /// not state - a compound assignment, an increment, a ref or out argument - counts as a
    /// write without a value, so the variable is never taken for one assigned once.
    /// </summary>
    private Variable Describe(ISymbol symbol)
    {
        if (described.TryGetValue(symbol, out var known))
        {
            return known;
        }

        var writes = new List<ExpressionSyntax?>();
        var reads = new List<IdentifierNameSyntax>();

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax { Initializer: { } initializer })
            {
                writes.Add(initializer.Value);
            }
        }

        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.Text != symbol.Name
                || !SymbolEqualityComparer.Default.Equals(SymbolOf(identifier), symbol))
            {
                continue;
            }

            switch (identifier.Parent)
            {
                case AssignmentExpressionSyntax assignment when assignment.Left == identifier:
                    writes.Add(assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ? assignment.Right : null);
                    break;

                case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression):
                case PostfixUnaryExpressionSyntax:
                    writes.Add(null);
                    break;

                case ArgumentSyntax argument when argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword):
                    writes.Add(null);
                    break;

                default:
                    reads.Add(identifier);
                    break;
            }
        }

        var variable = new Variable(writes, reads);
        described[symbol] = variable;
        return variable;
    }

    private SemanticModel Model => model ??= CSharpCompilation
        .Create("Snippet", [root.SyntaxTree])
        .GetSemanticModel(root.SyntaxTree);

    /// <summary>
    /// A local or a field - the latter is what a statement pasted outside any method parses
    /// as, being a member of the class the unit is wrapped in. A name bound to anything else
    /// holds nothing the unit assigned.
    /// </summary>
    private ISymbol? SymbolOf(IdentifierNameSyntax identifier)
    {
        var info = Model.GetSymbolInfo(identifier);
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length == 1 ? info.CandidateSymbols[0] : null);
        return symbol is ILocalSymbol or IFieldSymbol ? symbol : null;
    }

    private ISymbol? SymbolOf(VariableDeclaratorSyntax declarator)
        => Model.GetDeclaredSymbol(declarator) is { } symbol and (ILocalSymbol or IFieldSymbol) ? symbol : null;

    private sealed record Variable(IReadOnlyList<ExpressionSyntax?> Writes, IReadOnlyList<IdentifierNameSyntax> Reads);
}
