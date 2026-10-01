using AbstractWrappers;
using LinqParsing;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NHibernateWrappers;

/// <summary>
/// Reads an NHibernate LINQ query. LINQ is one of two query languages the framework is read
/// from: a C# unit comes here - a whole file or a fragment of one (decision 111) -, a bare HQL unit goes to
/// <see cref="NHibernateHqlQueryParser"/> (decisions 025 and 062) - one parser per language,
/// told apart by the content type the unit declares, never by what its text looks like.
/// </summary>
public class NHibernateLinqQueryParser(Func<AbstractQueryBuilder> queryBuilders) : LinqQueryParser(queryBuilders)
{
    /// <summary>
    /// NHibernate's provider joins the argument of StartsWith, EndsWith and Contains with
    /// the wildcard as written (its HQL generators concatenate, they do not escape), so
    /// <c>StartsWith("A_")</c> matches any character after A there, and the argument is the
    /// pattern's core verbatim. The opposite of EF Core, and a fact about the provider.
    /// </summary>
    protected override bool ProviderEscapesStringMethodArguments => false;

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
