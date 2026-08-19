using AbstractWrappers;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreWrappers;

/// <summary>
/// Reads an EF Core LINQ query. Everything about walking the chain lives in
/// <see cref="LinqQueryParser"/>, because it is System.Linq rather than EF Core
/// (decision 026); what is genuinely EF Core is only how a query starts.
/// </summary>
public class EFCoreLinqQueryParser(AbstractQueryBuilder queryBuilder) : LinqQueryParser(queryBuilder)
{
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

            // ctx.Customers - a DbSet property on the context. The context is recognised by
            // its position at the head of the chain, not by being called "ctx": a hard-coded
            // identifier made every other name silently unreadable.
            case MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax:
                root = new LinqQueryRoot(member.Name.Identifier.Text);
                return true;

            default:
                return false;
        }
    }
}
