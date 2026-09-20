using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Tests.Verification;

/// <summary>
/// Third verification level for an EF Core query (decision 027): EF Core translates the
/// generated LINQ into SQL. <c>ToQueryString()</c> runs the whole query pipeline and returns
/// the command text without opening a connection or executing anything, so an expression EF
/// Core cannot map fails here - the class of error no shape assertion sees.
/// </summary>
internal static class EFCoreQueryAcceptance
{
    /// <summary>
    /// Compiles nothing itself: it takes the assembly the query and entities were compiled
    /// into, finds the generated query method and returns the SQL EF Core would send.
    /// </summary>
    public static string Translate(byte[] compiled)
    {
        var assembly = Assembly.Load(compiled);

        var entityTypes = assembly.GetTypes()
            .Where(type => type.IsClass && type.IsPublic && !type.IsAbstract && type.Name != "GeneratedQueries")
            .ToList();

        // A fresh internal service provider per call, for the reason EFCoreAcceptance gives:
        // EF Core caches the model per context type and every verification shares this one.
        var options = new DbContextOptionsBuilder<QueryVerificationContext>()
            .UseSqlServer()
            .EnableServiceProviderCaching(false)
            .Options;

        using var context = new QueryVerificationContext(options, entityTypes);

        var method = FindQueryMethod(assembly);

        // A query with parameters declares them after the context (decision 083). Translation
        // is about the expression tree, not about the values, so nothing here picks a value
        // to mean anything - that would be a measurement, which this level is not - only one
        // the provider will not short-circuit away.
        object?[] arguments = [context, .. method.GetParameters().Skip(1).Select(DefaultOf)];

        var queryable = method.Invoke(null, arguments) as IQueryable
            ?? throw new InvalidOperationException("The generated query method did not return an IQueryable.");

        return queryable.ToQueryString();
    }

    /// <summary>
    /// The artifact's shape is part of its contract (decision 027): a public static method
    /// whose first parameter is a DbContext and which returns IQueryable. The parameters of
    /// the query follow it (decision 083), so the count is not fixed - only the first
    /// parameter is. AdvisorBenchmarking looks for the same shape, except that it still
    /// insists on exactly one parameter and therefore does not find a parameterized query;
    /// that is an open item, not a rule of the artifact.
    /// </summary>
    private static MethodInfo FindQueryMethod(Assembly assembly)
        => assembly.GetTypes()
               .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
               .FirstOrDefault(method =>
                   typeof(IQueryable).IsAssignableFrom(method.ReturnType)
                   && method.GetParameters() is [{ } parameter, ..]
                   && typeof(DbContext).IsAssignableFrom(parameter.ParameterType))
           ?? throw new InvalidOperationException(
               "No public static method taking a DbContext and returning IQueryable was generated.");

    /// <summary>
    /// A value to bind a query parameter to. Which value hardly matters for a condition -
    /// the expression tree is the same whatever number stands there - but it matters
    /// wherever EF Core short-circuits, because a query it never renders proves nothing.
    ///
    /// A collection therefore takes one element instead of none, and a number takes one
    /// instead of zero: EF Core folds a Take(0) into WHERE 0 = 1 and writes no slice at all,
    /// so a row count bound to the type's default (decision 085) would make the verdict
    /// vacuous exactly as an empty sequence does.
    /// </summary>
    private static object? DefaultOf(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            list.Add(element.IsValueType ? Activator.CreateInstance(element) : null);
            return list;
        }

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return Convert.ChangeType(1, type, CultureInfo.InvariantCulture);
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private sealed class QueryVerificationContext(
        DbContextOptions<QueryVerificationContext> options,
        IReadOnlyList<Type> entityTypes) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entityType in entityTypes)
            {
                modelBuilder.Entity(entityType);
            }
        }
    }
}
