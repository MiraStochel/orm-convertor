using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A compiled query of EF Core - <c>EF.CompileQuery</c> and <c>EF.CompileAsyncQuery</c>, the type
/// arguments written or not - is the query its lambda states over the context EF Core passes as
/// the first parameter (<c>EFCore/EFCoreCompiledQueryProviderTest</c>): read as the chain over
/// that context, its other parameters the query's. It used to be skipped without a word, the
/// member of a lambda parameter read as a navigation over objects in memory.
/// </summary>
public class CompiledQueryTest
{
    private const string File = "Compiled.cs";

    private const string Context = """
        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;
        }

        public class Order
        {
            public int OrderID { get; set; }

            public decimal Amount { get; set; }
        }


        """;

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [.. units]);

    private static ConversionSource Unit(string content, string name = File)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static string Compiled(string compiled) => $$"""
        public static class CompiledQueries
        {
            public static readonly Delegate Query = {{compiled}};
        }
        """;

    private static List<string> Queries(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).Select(s => s.Content)];

    [Theory]
    [InlineData("EF.CompileQuery((ShopContext c, decimal min) => c.Orders.Where(o => o.Amount > min))", "WHERE o.Amount > @min")]
    [InlineData("EF.CompileAsyncQuery((ShopContext c, decimal min) => c.Orders.Where(o => o.Amount > min))", "WHERE o.Amount > @min")]
    [InlineData("EF.CompileQuery<ShopContext, int, Order?>((c, id) => c.Orders.SingleOrDefault(o => o.OrderID == id))", "WHERE o.OrderID = @id")]
    [InlineData("EF.CompileAsyncQuery((ShopContext c, int id) => c.Orders.FirstOrDefault(o => o.OrderID == id))", "WHERE o.OrderID = @id")]
    [InlineData("Microsoft.EntityFrameworkCore.EF.CompileQuery((ShopContext c) => c.Set<Order>().Where(o => o.Amount > 100))", "WHERE o.Amount > 100")]
    [InlineData("EF.CompileQuery<ShopContext, Order>(c => c.Orders.OrderBy(o => o.Amount))", "ORDER BY o.Amount")]
    public void ACompiledQueryIsTheChainOverTheContext(string compiled, string hallmark)
    {
        var result = Convert(Unit(Context + Compiled(compiled)));

        var query = Assert.Single(Queries(result));
        Assert.Contains("FROM Orders AS o", query, StringComparison.Ordinal);
        Assert.Contains(hallmark, query, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>The set alone is the body EF Core compiles, so the whole table is the query.</summary>
    [Fact]
    public void ASetAloneIsTheWholeTable()
    {
        var query = Assert.Single(Queries(Convert(Unit(Context + Compiled("EF.CompileQuery((ShopContext c) => c.Orders)")))));

        Assert.Contains("FROM Orders AS o", query, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);
    }

    /// <summary>With the context in another file the set is known by its position, as in any chain over a context.</summary>
    [Fact]
    public void TheContextMayStandInAnotherFile()
    {
        var result = Convert(
            Unit(Context, "ShopContext.cs"),
            Unit(Compiled("EF.CompileQuery((ShopContext c, decimal min) => c.Orders.Where(o => o.Amount > min))")));

        Assert.Contains("WHERE o.Amount > @min", Assert.Single(Queries(result)), StringComparison.Ordinal);
    }

    /// <summary>A lambda handed to anything but EF Core's compilation keeps its parameter a row, and a member of it a navigation.</summary>
    [Fact]
    public void AnotherCompileIsNoCompiledQuery()
    {
        var result = Convert(Unit(Context + Compiled("Cache.CompileQuery((ShopContext c) => c.Orders.Where(o => o.Amount > 100))")));

        Assert.Empty(Queries(result));
    }
}
