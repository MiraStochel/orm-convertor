using AbstractWrappers;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NHibernateWrappers;

/// <summary>
/// Reads an NHibernate LINQ query. NHibernate is read from LINQ rather than from HQL
/// because HQL would mean either a hand-written grammar or a reference to NHibernate inside
/// the wrapper, which is what S1 forbids (decisions 022 and 026). As a target NHibernate
/// still emits HQL, so the framework is deliberately asymmetric.
/// </summary>
public class NHibernateLinqQueryParser(AbstractQueryBuilder queryBuilder) : LinqQueryParser(queryBuilder)
{
    protected override bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root)
    {
        root = null;

        // session.Query<Customer>() - the type argument names the entity, so unlike EF Core
        // there is no DbSet name to un-pluralize.
        if (expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax member
            && member.Name.Identifier.Text == "Query"
            && TypeArgumentOf(member.Name) is { } entity)
        {
            root = new LinqQueryRoot(entity);
            return true;
        }

        return false;
    }
}
