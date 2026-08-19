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
        var queryable = method.Invoke(null, [context]) as IQueryable
            ?? throw new InvalidOperationException("The generated query method did not return an IQueryable.");

        return queryable.ToQueryString();
    }

    /// <summary>
    /// The artifact's shape is part of its contract (decision 027): a public static method
    /// with a single DbContext parameter returning IQueryable. AdvisorBenchmarking looks for
    /// the same shape, which is why the builder emits it rather than a method returning a list.
    /// </summary>
    private static MethodInfo FindQueryMethod(Assembly assembly)
        => assembly.GetTypes()
               .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
               .FirstOrDefault(method =>
                   typeof(IQueryable).IsAssignableFrom(method.ReturnType)
                   && method.GetParameters() is [{ } parameter]
                   && typeof(DbContext).IsAssignableFrom(parameter.ParameterType))
           ?? throw new InvalidOperationException(
               "No public static method taking a DbContext and returning IQueryable was generated.");

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
