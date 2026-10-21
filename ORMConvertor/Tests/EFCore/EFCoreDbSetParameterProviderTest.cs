using System.Reflection;
using Common.Compilation;
using Microsoft.EntityFrameworkCore;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What the reading of a name declared as a set rests on, measured against EF Core 10: a chain
/// over a parameter declared DbSet&lt;Order&gt; sends the query a chain over Set&lt;Order&gt;() sends,
/// whichever set of the context the caller passes, so the declared type states the query and the
/// name says nothing; Set&lt;T&gt;() over a type parameter reads the table of the type the caller
/// supplies; and the methods of a set that return no query and no entity only track an entity,
/// so a chain that starts with one hands the provider nothing to read
/// (<c>EFCoreLinqQueryParser.IsProviderTrackingCall</c>).
/// </summary>
public class EFCoreDbSetParameterProviderTest
{
    private const string Source = """
        using System.ComponentModel.DataAnnotations;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace DeclaredSet;

        public class Order
        {
            [Key] public int OrderId { get; set; }

            public decimal Amount { get; set; }
        }

        public class Customer { [Key] public int CustomerId { get; set; } }

        public class ShopContext(DbContextOptions options) : DbContext(options)
        {
            public DbSet<Order> Orders { get; set; } = null!;

            public DbSet<Customer> Customers { get; set; } = null!;
        }

        public static class Queries
        {
            public static IQueryable<Order> Big(DbSet<Order> orders) => orders.Where(o => o.Amount > 100);

            public static IQueryable<Order> BigOfTheContext(ShopContext ctx) => ctx.Set<Order>().Where(o => o.Amount > 100);

            public static IQueryable<T> All<T>(ShopContext ctx) where T : class => ctx.Set<T>();
        }
        """;

    private static (DbContext Context, Assembly Assembly) Context()
    {
        var result = CSharpSourceCompiler.Compile("EFCoreDbSetParameter", [Source], GeneratedQueryCompiler.EFCoreConsumerReferences);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));

        var assembly = Assembly.Load(result.Assembly!);
        var options = new DbContextOptionsBuilder().UseSqlServer().EnableServiceProviderCaching(false).Options;
        return ((DbContext)Activator.CreateInstance(assembly.GetType("DeclaredSet.ShopContext")!, options)!, assembly);
    }

    private static string Sql(Assembly assembly, string method, params object[] arguments)
        => ((IQueryable)assembly.GetType("DeclaredSet.Queries")!.GetMethod(method)!.Invoke(null, arguments)!).ToQueryString();

    [Fact]
    public void AChainOverASetParameterIsTheChainOverSetOfItsEntity()
    {
        var (context, assembly) = Context();
        using var _ = context;
        var order = assembly.GetType("DeclaredSet.Order")!;

        var byProperty = Sql(assembly, "Big", context.GetType().GetProperty("Orders")!.GetValue(context)!);
        var bySet = Sql(assembly, "Big", typeof(DbContext).GetMethod("Set", Type.EmptyTypes)!.MakeGenericMethod(order).Invoke(context, null)!);

        Assert.Equal(Sql(assembly, "BigOfTheContext", context), byProperty);
        Assert.Equal(byProperty, bySet);
        Assert.Contains("FROM [Orders] AS [o]", byProperty, StringComparison.Ordinal);
        Assert.Contains("WHERE [o].[Amount] > 100.0", byProperty, StringComparison.Ordinal);
    }

    [Fact]
    public void SetOverATypeParameterReadsTheTableOfTheTypeTheCallerSupplies()
    {
        var (context, assembly) = Context();
        using var _ = context;
        var all = assembly.GetType("DeclaredSet.Queries")!.GetMethod("All")!;

        var orders = ((IQueryable)all.MakeGenericMethod(assembly.GetType("DeclaredSet.Order")!).Invoke(null, [context])!).ToQueryString();
        var customers = ((IQueryable)all.MakeGenericMethod(assembly.GetType("DeclaredSet.Customer")!).Invoke(null, [context])!).ToQueryString();

        Assert.Contains("FROM [Orders]", orders, StringComparison.Ordinal);
        Assert.Contains("FROM [Customers]", customers, StringComparison.Ordinal);
    }

    /// <summary>
    /// The public methods a set declares, sorted by what they return: a query or its enumeration
    /// (IQueryable, IAsyncEnumerable, the async enumerator), the entity (Find), or neither - and
    /// the last are exactly the calls the reading takes for tracking, so a method EF Core adds
    /// fails here until it is sorted.
    /// </summary>
    [Fact]
    public void TheMethodsOfASetThatReturnNoQueryAndNoEntityOnlyTrack()
    {
        var set = typeof(DbSet<>);
        var entity = set.GetGenericArguments()[0];

        var tracking = set.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName && method.GetBaseDefinition().DeclaringType != typeof(object))
            .Where(method => !ReturnsAQuery(method.ReturnType) && !ReturnsTheEntity(method.ReturnType, entity))
            .Select(method => method.Name)
            .ToHashSet();

        Assert.Equal(
            ["Add", "AddAsync", "AddRange", "AddRangeAsync", "Attach", "AttachRange", "Entry", "Remove", "RemoveRange", "Update", "UpdateRange"],
            tracking.Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);

        Assert.All(["Add", "Attach", "Update", "Remove", "Entry"], name =>
            Assert.Equal("EntityEntry`1", set.GetMethod(name, [entity])!.ReturnType.Name));
    }

    /// <summary>The view of the entities a set tracks is a collection in memory, not a query.</summary>
    [Fact]
    public void TheLocalViewOfASetIsNoQuery()
        => Assert.False(typeof(IQueryable).IsAssignableFrom(typeof(DbSet<>).GetProperty("Local")!.PropertyType));

    private static bool ReturnsAQuery(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() is var definition
           && (definition == typeof(IQueryable<>) || definition == typeof(IAsyncEnumerable<>) || definition == typeof(IAsyncEnumerator<>));

    private static bool ReturnsTheEntity(Type type, Type entity)
        => type == entity
           || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>) && type.GetGenericArguments()[0] == entity);
}
