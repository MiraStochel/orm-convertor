using AbstractWrappers;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreWrappers;

/// <summary>
/// Reads an EF Core LINQ query. Everything about walking the chain lives in
/// <see cref="LinqQueryParser"/>, because it is System.Linq rather than EF Core
/// (decision 026); what is genuinely EF Core is how a query starts and, for a pattern,
/// its own <c>EF.Functions.Like</c> - the string methods are System.String and the shared
/// reader reads them, under the provider's default of escaping their argument.
/// </summary>
public class EFCoreLinqQueryParser(Func<AbstractQueryBuilder> queryBuilders) : LinqQueryParser(queryBuilders)
{
    /// <summary>
    /// <c>EF.Functions.Like(column, pattern)</c> and the overload with the escape - the
    /// shape the EF Core builder writes where the split of decision 051 is not exact, so
    /// the one the identity direction has to read back. Recognized by the last two names of
    /// the receiver, so that the qualified spelling is the same call.
    /// </summary>
    protected override bool TryReadProviderPatternFunction(
        InvocationExpressionSyntax invocation,
        out ExpressionSyntax? column,
        out ExpressionSyntax? pattern,
        out ExpressionSyntax? escape)
    {
        column = null;
        pattern = null;
        escape = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Like",
                Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "Functions" } functions,
            }
            || LastIdentifier(functions.Expression) != "EF")
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count is not (2 or 3))
        {
            return false;
        }

        column = arguments[0].Expression;
        pattern = arguments[1].Expression;
        escape = arguments.Count == 3 ? arguments[2].Expression : null;
        return true;
    }

    private static string? LastIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        AliasQualifiedNameSyntax qualified => qualified.Name.Identifier.Text,
        _ => null,
    };

    protected override bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root)
    {
        root = null;

        switch (expression)
        {
            // ctx.Set<Customer>() - the type argument names the entity outright.
            case InvocationExpressionSyntax invocation
                when invocation.Expression is MemberAccessExpressionSyntax setAccess
                     && setAccess.Name.Identifier.Text == "Set"
                     && TypeArgumentOf(setAccess.Name) is { } entity:
                root = new LinqQueryRoot(entity);
                return true;

            // ctx.Customers - a DbSet property on the context. The context is recognized by
            // its position at the head of the chain, not by being called "ctx": a hard-coded
            // identifier made every other name silently unreadable.
            case MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax:
                root = new LinqQueryRoot(member.Name.Identifier.Text);
                return true;

            // Inside the context, its own DbSet is the root the same way ctx.Customers is from
            // outside - by bare name, through this, or as Set<T>() (decision 111). Anywhere
            // else a member of the class's own instance is a navigation over what the instance
            // loaded, which no provider translates: Lines.Sum(…) in an entity is LINQ over
            // objects in memory, not a query.
            case IdentifierNameSyntax own when EFCoreContext.IsOwnDbSet(own, own.Identifier.Text):
                root = new LinqQueryRoot(own.Identifier.Text);
                return true;

            case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } own
                when EFCoreContext.IsOwnDbSet(own, own.Name.Identifier.Text):
                root = new LinqQueryRoot(own.Name.Identifier.Text);
                return true;

            case InvocationExpressionSyntax { Expression: GenericNameSyntax { Identifier.Text: "Set" } set } bare
                when EFCoreContext.Around(bare) is not null && TypeArgumentOf(set) is { } ownEntity:
                root = new LinqQueryRoot(ownEntity);
                return true;

            default:
                return false;
        }
    }
}
