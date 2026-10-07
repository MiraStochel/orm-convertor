using System.Reflection;
using NHibernate;
using NHibernate.Engine;
using NHibernate.Linq;

namespace Tests.Verification;

/// <summary>
/// Third verification level of decision 027 for the LINQ form of an NHibernate query
/// (decision 118): the generated method is called with a session that has no connection,
/// and the chain it returns is handed to the provider of NHibernate 5.7.0, which translates
/// it into HQL and compiles the query plan against the mapped model - the same plan the HQL
/// acceptance compiles, so the same refusals: a member no mapping declares, a shape the
/// provider does not translate. Nothing is executed.
/// </summary>
internal static class NHibernateLinqAcceptance
{
    /// <summary>
    /// Translates the LINQ form and returns the SQL NHibernate writes for it, still without a
    /// connection. Throws whatever the provider throws when it refuses the chain.
    /// </summary>
    /// <param name="compiled">The entities and the generated method, compiled together (<see cref="GeneratedQueryCompiler"/>).</param>
    /// <param name="arguments">The arguments after the session, in the order of the method's parameters (decision 083).</param>
    public static string Sql(byte[] compiled, IEnumerable<string> mappingXmls, params object?[] arguments)
    {
        var (factory, assembly) = NHibernateAcceptance.OpenSessionFactory(compiled, mappingXmls);

        // The provider registers the entity types it meets once per process, by name
        // (NHibernateTypeRegistry); the names recur across the assemblies this suite compiles.
        NHibernateTypeRegistry.Forget(assembly);

        using (factory)
        using (var session = factory.OpenSession())
        {
            var method = assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

            var queryable = (IQueryable)method.Invoke(null, [session, .. arguments])!;

            var implementor = (ISessionFactoryImplementor)factory;
            var expression = new NhLinqExpression(queryable.Expression, implementor);
            var plan = implementor.QueryPlanCache.GetHQLQueryPlan(expression, false, new Dictionary<string, IFilter>());

            return string.Join(Environment.NewLine, plan.Translators.Select(translator => translator.SQLString));
        }
    }
}
