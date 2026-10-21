using System.Reflection;
using Common.Compilation;
using Microsoft.EntityFrameworkCore;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// What the reading of a set handed over whole rests on, measured against EF Core 10 and the C#
/// compiler of the solution: a set a member returns as an IQueryable is the query over the whole
/// table, with nothing composed over it; and a collection navigation in memory does not convert
/// to IQueryable&lt;T&gt;, while it converts to IEnumerable&lt;T&gt; as the set does. So a value given a
/// query type is a query and never a list, and IEnumerable states nothing either way.
/// </summary>
public class EFCoreSetHandedOverProviderTest
{
    private const string Entities = """
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace HandedOver;

        public class Order
        {
            [Key] public int OrderId { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }

        public class OrderLine { [Key] public int OrderLineId { get; set; } }

        """;

    [Fact]
    public void ASetReturnedAsAQueryIsTheWholeTable()
    {
        var result = CSharpSourceCompiler.Compile(
            "EFCoreSetHandedOver",
            [Entities + """
                public class ShopContext(DbContextOptions options) : DbContext(options)
                {
                    public DbSet<Order> Orders { get; set; } = null!;

                    public IQueryable<Order> All() => Orders;
                }
                """],
            GeneratedQueryCompiler.EFCoreConsumerReferences);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors.Select(e => e.ToString())));

        var options = new DbContextOptionsBuilder().UseSqlServer().EnableServiceProviderCaching(false).Options;
        var type = Assembly.Load(result.Assembly!).GetType("HandedOver.ShopContext")!;
        using var context = (DbContext)Activator.CreateInstance(type, options)!;

        var sql = ((IQueryable)type.GetMethod("All")!.Invoke(context, null)!).ToQueryString();

        Assert.Contains("FROM [Orders] AS [o]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IQueryable<OrderLine>", false)]
    [InlineData("IOrderedQueryable<OrderLine>", false)]
    [InlineData("DbSet<OrderLine>", false)]
    [InlineData("IEnumerable<OrderLine>", true)]
    public void ACollectionInMemoryConvertsToNoQueryType(string declared, bool converts)
    {
        var result = CSharpSourceCompiler.Compile(
            "EFCoreNavigationAsAQuery",
            [Entities + $$"""
                public static class Walk
                {
                    public static {{declared}} Lines(Order order) => order.Lines;
                }
                """],
            GeneratedQueryCompiler.EFCoreConsumerReferences);

        Assert.Equal(converts, result.Success);
    }

    /// <summary>
    /// The static classes EF Core keeps its query steps in, which the reading refuses a query
    /// handed to in their static form (<c>EFCoreLinqQueryParser.IsProviderStaticQueryClass</c>):
    /// <c>EntityFrameworkQueryableExtensions.CountAsync(q)</c> is the count of q, and a query read
    /// without it would answer rows.
    /// </summary>
    [Theory]
    [InlineData(typeof(EntityFrameworkQueryableExtensions), "ToListAsync")]
    [InlineData(typeof(EntityFrameworkQueryableExtensions), "CountAsync")]
    [InlineData(typeof(EntityFrameworkQueryableExtensions), "Include")]
    [InlineData(typeof(EntityFrameworkQueryableExtensions), "ExecuteDelete")]
    [InlineData(typeof(RelationalQueryableExtensions), "FromSqlRaw")]
    [InlineData(typeof(RelationalQueryableExtensions), "AsSplitQuery")]
    public void TheStepIsAStaticMethodOfTheClass(Type owner, string step)
        => Assert.Contains(owner.GetMethods(), method => method.IsStatic && method.Name == step);
}
