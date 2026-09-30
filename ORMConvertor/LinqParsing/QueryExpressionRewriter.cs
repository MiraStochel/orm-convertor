using AbstractWrappers.Descriptors;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace LinqParsing;

/// <summary>
/// Rewrites every query expression in a syntax tree into the method chain the C#
/// specification defines it as (decision 103). A query expression is not a second query
/// language: the specification (§12.20.3, "Query expression translation") gives it meaning
/// only as a syntactic rewrite onto the same <c>Where</c>, <c>OrderBy</c>, <c>Join</c>,
/// <c>GroupBy</c> and <c>Select</c> the chain parser reads, so the reading here is that
/// rewrite, mechanically and without a semantic model, and the parser then sees a chain it
/// would have seen had the query been written by hand.
///
/// The one thing the rewrite has to keep track of is the transparent identifier: after a
/// join or a second <c>from</c> that is not the last clause, the clauses that follow range
/// over a row composed of both range variables (<c>new { c, o }</c>) under one lambda
/// parameter, and a range variable written bare in those clauses is a member of that row
/// (<c>c.Name</c> becomes <c>t.c.Name</c>). That composed row is exactly the joined row the
/// chain parser reads out of a result selector, and the EF Core builder's own spelling of it.
///
/// Inner query expressions are rewritten before the clause holding them is copied into a
/// lambda, so a subquery in a predicate arrives as the chain the operand reader expects. A
/// <c>let</c> clause is refused by name: it introduces a computed range variable, which the
/// query representation has no place for, and the rewrite stops there so that the clauses
/// after it - which would read its variable as a value from the enclosing scope - do not go
/// out as a query saying something else.
/// </summary>
internal sealed class QueryExpressionRewriter : CSharpSyntaxRewriter
{
    private readonly HashSet<string> used;
    private int fresh;

    /// <summary>What the rewrite refused, for the parser to report on the unit.</summary>
    public List<(string Reason, QueryFeature Feature)> Refusals { get; } = [];

    /// <summary>Whether the tree held a query expression at all.</summary>
    public bool Rewrote { get; private set; }

    public QueryExpressionRewriter(SyntaxNode root)
    {
        used = root.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.Text)
            .ToHashSet(StringComparer.Ordinal);
    }

    public override SyntaxNode? VisitQueryExpression(QueryExpressionSyntax node)
    {
        // Inner first: a query expression nested in a clause is a chain by the time the
        // clause becomes a lambda.
        var visited = (QueryExpressionSyntax)base.VisitQueryExpression(node)!;
        Rewrote = true;

        // `from T x in e` casts the elements; the type says what they are, and the rows of
        // an entity set are its entities already, so the cast adds nothing the parser reads.
        var from = visited.FromClause;
        var state = new Translation(from.Expression, from.Identifier.Text);
        return TranslateBody(state, visited.Body);
    }

    private ExpressionSyntax TranslateBody(Translation state, QueryBodySyntax body)
    {
        var clauses = body.Clauses;
        var selectFolded = false;

        for (var i = 0; i < clauses.Count; i++)
        {
            switch (clauses[i])
            {
                case WhereClauseSyntax where:
                    state.Chain = Call(state.Chain, "Where", state.Lambda(where.Condition));
                    break;

                case OrderByClauseSyntax orderBy:
                    for (var k = 0; k < orderBy.Orderings.Count; k++)
                    {
                        var ordering = orderBy.Orderings[k];
                        var method = (k == 0 ? "OrderBy" : "ThenBy")
                            + (ordering.AscendingOrDescendingKeyword.IsKind(SyntaxKind.DescendingKeyword) ? "Descending" : string.Empty);
                        state.Chain = Call(state.Chain, method, state.Lambda(ordering.Expression));
                    }

                    break;

                case JoinClauseSyntax join:
                    {
                        var inner = join.Identifier.Text;
                        var outerKey = state.Lambda(join.LeftExpression);

                        // The right side of `equals` ranges over the inner variable alone;
                        // C# lets nothing else in.
                        var innerKey = SimpleLambdaExpression(Parameter(Identifier(inner)), join.RightExpression);

                        // A join that is the last clause before `select` takes the selected
                        // shape as its result selector; any other join composes both rows
                        // under a transparent identifier for the clauses that follow. A
                        // `join … into` is a group join, whose second row is the group.
                        if (join.Into is null && i == clauses.Count - 1 && body.SelectOrGroup is SelectClauseSyntax folded)
                        {
                            var selector = state.ResultSelector(inner, folded.Expression);
                            state.Chain = Call(state.Chain, "Join", join.InExpression, outerKey, innerKey, selector);
                            selectFolded = true;
                        }
                        else
                        {
                            var second = join.Into?.Identifier.Text ?? inner;
                            var selector = ComposeBoth(state.Parameter, second);
                            state.Chain = Call(state.Chain, join.Into is null ? "Join" : "GroupJoin", join.InExpression, outerKey, innerKey, selector);
                            state.Compose(second, Fresh());
                        }

                        break;
                    }

                case FromClauseSyntax from:
                    {
                        // A second `from` is SelectMany over the first row. Over a collection
                        // of that row it is the join along an association path, which the
                        // parser derives from the relation (decision 101); over a second
                        // source it is a cross join, which the parser refuses by name.
                        var inner = from.Identifier.Text;
                        var collection = state.Lambda(from.Expression);
                        var selector = ComposeBoth(state.Parameter, inner);
                        state.Chain = Call(state.Chain, "SelectMany", collection, selector);
                        state.Compose(inner, Fresh());
                        break;
                    }

                case LetClauseSyntax let:
                    Refusals.Add((
                        $"The clause 'let {let.Identifier.Text} = {let.Expression}' introduces a computed range variable, which the query representation has no place for; no artifact was generated.",
                        QueryFeature.Projection));
                    return state.Chain;
            }
        }

        if (!selectFolded)
        {
            switch (body.SelectOrGroup)
            {
                case SelectClauseSyntax select:
                    state.Chain = Call(state.Chain, "Select", state.Lambda(select.Expression));
                    break;

                case GroupClauseSyntax group:
                    {
                        var key = state.Lambda(group.ByExpression);
                        state.Chain = state.IsTheRangeVariable(group.GroupExpression)
                            ? Call(state.Chain, "GroupBy", key)
                            : Call(state.Chain, "GroupBy", key, state.Lambda(group.GroupExpression));
                        break;
                    }
            }
        }

        // `into x` after a select or group starts the rest of the query over the result,
        // with x as its one range variable.
        if (body.Continuation is { } continuation)
        {
            return TranslateBody(new Translation(state.Chain, continuation.Identifier.Text), continuation.Body);
        }

        return state.Chain;
    }

    private static InvocationExpressionSyntax Call(ExpressionSyntax receiver, string method, params ExpressionSyntax[] arguments)
        => InvocationExpression(
            MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, receiver, IdentifierName(method)),
            ArgumentList(SeparatedList(arguments.Select(Argument))));

    /// <summary><c>(a, b) =&gt; new { a, b }</c>: both rows, under their own names.</summary>
    private static ParenthesizedLambdaExpressionSyntax ComposeBoth(string first, string second)
        => ParenthesizedLambdaExpression(
            ParameterList(SeparatedList([Parameter(Identifier(first)), Parameter(Identifier(second))])),
            AnonymousObjectCreationExpression(SeparatedList(
            [
                AnonymousObjectMemberDeclarator(IdentifierName(first)),
                AnonymousObjectMemberDeclarator(IdentifierName(second)),
            ])));

    /// <summary>
    /// A name no identifier in the tree uses, for the transparent identifier. It starts at
    /// t, which is the name the EF Core builder gives the joined row, so that the chain the
    /// rewrite produces reads like the one the tool itself writes.
    /// </summary>
    private string Fresh()
    {
        while (true)
        {
            var name = fresh == 0 ? "t" : $"t{fresh}";
            fresh++;

            if (used.Add(name))
            {
                return name;
            }
        }
    }

    /// <summary>
    /// The chain built so far, the parameter the next lambda ranges over, and where every
    /// range variable of the query is found from that parameter: itself while there is one
    /// range variable, a member path of the transparent identifier after a join composed
    /// the rows.
    /// </summary>
    private sealed class Translation
    {
        private Dictionary<string, ExpressionSyntax> scope;
        private bool transparent;

        public ExpressionSyntax Chain;
        public string Parameter;

        public Translation(ExpressionSyntax chain, string rangeVariable)
        {
            Chain = chain;
            Parameter = rangeVariable;
            scope = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal)
            {
                [rangeVariable] = IdentifierName(rangeVariable),
            };
        }

        public bool IsTheRangeVariable(ExpressionSyntax expression)
            => !transparent
               && expression is IdentifierNameSyntax identifier
               && identifier.Identifier.Text == Parameter;

        public SimpleLambdaExpressionSyntax Lambda(ExpressionSyntax body)
            => SimpleLambdaExpression(Parameter(Identifier(Parameter)), Substitute(body, scope));

        /// <summary>
        /// <c>(p, inner) =&gt; selected</c>, with the range variables of the clauses so far
        /// reached through p and the joined variable by its own name.
        /// </summary>
        public ParenthesizedLambdaExpressionSyntax ResultSelector(string inner, ExpressionSyntax selected)
        {
            var withInner = new Dictionary<string, ExpressionSyntax>(scope, StringComparer.Ordinal)
            {
                [inner] = IdentifierName(inner),
            };

            return ParenthesizedLambdaExpression(
                ParameterList(SeparatedList([Parameter(Identifier(Parameter)), Parameter(Identifier(inner))])),
                Substitute(selected, withInner));
        }

        /// <summary>
        /// After <c>(p, second) =&gt; new { p, second }</c>: the next lambdas range over the
        /// composed row under a fresh name, every earlier variable sits one member deeper
        /// (<c>t.p.…</c>), and the second row is the member of its own name.
        /// </summary>
        public void Compose(string second, string next)
        {
            var root = IdentifierName(next);
            var composed = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);

            foreach (var (name, path) in scope)
            {
                composed[name] = ReplaceRoot(path, MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, root, IdentifierName(Parameter)));
            }

            composed[second] = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, root, IdentifierName(second));

            scope = composed;
            Parameter = next;
            transparent = true;
        }

        private static ExpressionSyntax ReplaceRoot(ExpressionSyntax path, ExpressionSyntax replacement) => path switch
        {
            MemberAccessExpressionSyntax member => member.WithExpression(ReplaceRoot(member.Expression, replacement)),
            _ => replacement,
        };

        private static ExpressionSyntax Substitute(ExpressionSyntax body, IReadOnlyDictionary<string, ExpressionSyntax> scope)
        {
            var identity = scope.All(entry => entry.Value is IdentifierNameSyntax name && name.Identifier.Text == entry.Key);
            return identity ? body : (ExpressionSyntax)new Substitution(scope).Visit(body)!;
        }
    }

    /// <summary>
    /// Replaces a range variable written bare by its path from the transparent identifier.
    /// A name in member position (<c>x.Name</c>, <c>?.Name</c>, <c>N = …</c>, a named
    /// argument, a type) is not a variable and is left alone.
    /// </summary>
    private sealed class Substitution(IReadOnlyDictionary<string, ExpressionSyntax> scope) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node
                || node.Parent is MemberBindingExpressionSyntax
                || node.Parent is NameEqualsSyntax
                || node.Parent is NameColonSyntax
                || node.Parent is QualifiedNameSyntax
                || node.Parent is TypeArgumentListSyntax
                || node.Parent is ObjectCreationExpressionSyntax creation && creation.Type == node)
            {
                return node;
            }

            return scope.TryGetValue(node.Identifier.Text, out var path)
                ? path.WithTriviaFrom(node)
                : node;
        }
    }
}
