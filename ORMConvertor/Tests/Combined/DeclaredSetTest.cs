using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A name the unit declares <c>DbSet&lt;Order&gt;</c> - a parameter, a local or a field whose value
/// the text does not fix - is the set of Order whatever supplies the value, as decision 114 reads
/// a member so declared and decision 158 the value of a property so declared: a chain over it, its
/// enumeration and its hand-over are the queries <c>Set&lt;Order&gt;()</c> would give, under the table
/// the mapping gives Order, whatever the name says (<c>EFCore/EFCoreDbSetParameterProviderTest</c>).
/// A chain over such a parameter used to vanish without a word, and the class around came out as
/// an entity. A call over any set that only tracks an entity - Add, Remove, Entry - is no query;
/// it used to come out as a query over the whole table.
/// </summary>
public class DeclaredSetTest
{
    private const string File = "Queries.cs";

    private const string Model = """
        public class Order
        {
            public int OrderID { get; set; }

            public int CustomerID { get; set; }

            public decimal Amount { get; set; }
        }

        public class Customer
        {
            public int CustomerID { get; set; }
        }

        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;

            public DbSet<Customer> Customers { get; set; } = null!;
        }


        """;

    private static ConversionResult Convert(string content)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [new ConversionSource { ContentType = ConversionContentType.CSharp, Content = Model + content, Name = File }]);

    private static string Static(string code) => "public static class Queries\n{\n    " + code + "\n}\n";

    private static List<string> Queries(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).Select(s => s.Content)];

    private static List<ConversionRecord> Failures(ConversionResult result)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure)];

    /// <summary>What the same query gives written over Set&lt;Order&gt;() of the context.</summary>
    private static string OverTheContext(string chain)
        => Assert.Single(Queries(Convert(Static("public static List<Order> Big(ShopContext ctx) => ctx.Set<Order>()" + chain + ";"))));

    [Theory]
    [InlineData("public static List<Order> Big(DbSet<Order> orders) => orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public static List<Order> Big(DbSet<Order> x) => x.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public static List<Order> Big(DbSet<Order>? orders) => orders!.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public static List<Order> Big(Microsoft.EntityFrameworkCore.DbSet<Order> orders) => orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public static List<Order> Big(DbSet<Order> orders) { var big = orders.Where(o => o.Amount > 100); return big.ToList(); }")]
    [InlineData("public static List<Order> Big(Factory factory) { DbSet<Order> orders = factory.Orders(); return orders.Where(o => o.Amount > 100).ToList(); }")]
    [InlineData("public static Func<DbSet<Order>, List<Order>> Big = (DbSet<Order> orders) => orders.Where(o => o.Amount > 100).ToList();")]
    public void AChainOverASetParameterOrLocalIsTheChainOverSetOfItsEntity(string code)
    {
        var result = Convert(Static(code));

        Assert.Equal(OverTheContext(".Where(o => o.Amount > 100).ToList()"), Assert.Single(Queries(result)));
        Assert.Empty(Failures(result));
    }

    /// <summary>A set a service gets from its constructor, kept in a field or not, written bare or through this: the service is the code around its queries.</summary>
    [Theory]
    [InlineData("public List<Order> Big() => orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public List<Order> Big() => _orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public List<Order> Big() => this._orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public List<Order> Big() => _unset.Where(o => o.Amount > 100).ToList();")]
    public void AChainOverASetAServiceIsGivenIsTheChainOverSetOfItsEntity(string code)
    {
        var result = Convert($$"""
            public class OrderService(DbSet<Order> orders)
            {
                private readonly DbSet<Order> _orders = orders;

                private readonly DbSet<Order> _unset = null!;

                {{code}}
            }
            """);

        Assert.Equal(OverTheContext(".Where(o => o.Amount > 100).ToList()"), Assert.Single(Queries(result)));
        Assert.Empty(Failures(result));
        Assert.Contains(result.Records, r => r.Reason.Contains("'OrderService'", StringComparison.Ordinal) && r.Reason.Contains("not as an entity", StringComparison.Ordinal));
    }

    /// <summary>Enumerated, returned as a query or passed on whole, the set is the whole table - as Set&lt;Order&gt;() handed over the same way is.</summary>
    [Theory]
    [InlineData("public static int Count(DbSet<Order> orders) { var n = 0; foreach (var o in orders) n++; return n; }")]
    [InlineData("public static List<Order> All(DbSet<Order> orders) => [.. orders];")]
    [InlineData("public static IQueryable<Order> All(DbSet<Order> orders) => orders;")]
    [InlineData("public static void All(DbSet<Order> orders) => Report.Print(orders);")]
    public void ASetParameterHandedOverWholeIsTheWholeTable(string code)
    {
        var result = Convert(Static(code));

        var query = Assert.Single(Queries(result));
        Assert.Contains("FROM Orders AS o", query, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);
        Assert.Empty(Failures(result));
    }

    /// <summary>Joined, the set is the table of its entity, not one named after the parameter.</summary>
    [Fact]
    public void AJoinedSetParameterIsTheTableOfItsEntity()
    {
        var result = Convert(Static("public static List<Order> Joined(ShopContext ctx, DbSet<Order> x) => ctx.Customers.Join(x, c => c.CustomerID, o => o.CustomerID, (c, o) => o).ToList();"));

        Assert.Contains("JOIN Orders o", Assert.Single(Queries(result)), StringComparison.Ordinal);
        Assert.Empty(Failures(result));
    }

    /// <summary>FromSql over the parameter is the native query it is over Set&lt;Order&gt;() (decision 113).</summary>
    [Fact]
    public void FromSqlOverASetParameterIsTheNativeQuery()
    {
        const string sql = "\"SELECT * FROM Orders WHERE Amount > 100\"";
        var result = Convert(Static($"public static List<Order> Big(DbSet<Order> orders) => orders.FromSqlRaw({sql}).ToList();"));

        Assert.Equal(OverTheContext($".FromSqlRaw({sql}).ToList()"), Assert.Single(Queries(result)));
        Assert.Empty(Failures(result));
    }

    /// <summary>The view of the entities a set tracks is a collection in memory, no set of its own.</summary>
    [Fact]
    public void TheLocalViewOfASetParameterIsNoQuery()
    {
        var result = Convert(Static("public static int Tracked(DbSet<Order> orders) { var n = 0; foreach (var o in orders.Local) n++; return n; }"));

        Assert.Empty(Queries(result));
        Assert.Empty(Failures(result));
    }

    /// <summary>A call that only tracks an entity hands the provider nothing to read, over any set, with or without await.</summary>
    [Theory]
    [InlineData("public static void Add(ShopContext ctx, Order o) => ctx.Set<Order>().Add(o);")]
    [InlineData("public static void Remove(ShopContext ctx, Order o) { ctx.Set<Order>().Remove(o); ctx.SaveChanges(); }")]
    [InlineData("public static void AddAll(ShopContext ctx, List<Order> os) => ctx.Set<Order>().AddRange(os);")]
    [InlineData("public static void Detach(ShopContext ctx, Order o) => ctx.Set<Order>().Entry(o).State = EntityState.Detached;")]
    [InlineData("public static void Add(ShopContext ctx, Order o) { var set = ctx.Set<Order>(); set.Add(o); }")]
    [InlineData("public static async Task Add(DbSet<Order> orders, Order o) => await orders.AddAsync(o);")]
    [InlineData("public static void Attach(DbSet<Order> orders, Order o) => orders.Attach(o);")]
    [InlineData("public static void Update(ShopContext ctx, Order o) => ctx.Orders.Update(o);")]
    public void ACallThatOnlyTracksIsNoQuery(string code)
    {
        var result = Convert(Static(code));

        Assert.Empty(Queries(result));
        Assert.Empty(Failures(result));
    }

    /// <summary>Inside the context, its own set and Set&lt;T&gt;() track the same way.</summary>
    [Theory]
    [InlineData("public void Keep(Order o) => Set<Order>().Update(o);")]
    [InlineData("public void Keep(Order o) => Orders.Add(o);")]
    [InlineData("public void Keep(Order o) => this.Orders.Remove(o);")]
    public void TheContextsOwnSetTracksTheSameWay(string code)
    {
        var result = Convert("public class TrackingContext : DbContext\n{\n    public DbSet<Order> Orders { get; set; } = null!;\n\n    " + code + "\n}\n");

        Assert.Empty(Queries(result));
        Assert.Empty(Failures(result));
    }

    /// <summary>A tracking call takes no query with it: one passed to AddRange is read where it stands.</summary>
    [Fact]
    public void AQueryATrackingCallIsGivenIsReadWhereItStands()
    {
        var result = Convert(Static("public static void Copy(ShopContext ctx) => ctx.Set<Order>().AddRange(ctx.Orders.Where(o => o.Amount > 100).ToList());"));

        Assert.Equal(OverTheContext(".Where(o => o.Amount > 100).ToList()"), Assert.Single(Queries(result)));
    }
}
