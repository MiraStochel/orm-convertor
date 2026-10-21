using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A set written as a name with no step over it is a query where the code hands it over whole -
/// a member returns it, a property is given it, a call or a constructor gets it as an argument,
/// a spread enumerates it -, as Set&lt;T&gt;() handed over the same way is (decisions 109, 111
/// and 158). Where the unit declares the set - the context's own, a member of a context it
/// declares - every such place is a query. One known only by its position (decision 114) is a
/// query where the unit states a query type for the place it goes to: IQueryable or DbSet, which
/// a collection in memory does not convert to (<c>EFCore/EFCoreSetHandedOverProviderTest</c>).
/// Every one of them used to vanish without a word, and the class around came out as an entity.
/// </summary>
public class HandedOverSetTest
{
    private const string File = "Repo.cs";

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [.. units]);

    private static ConversionSource Unit(string content, string name = File)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static List<ConversionRecord> NotEntities(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.Contains("not as an entity", StringComparison.Ordinal))];

    private static void AssertTheWholeTable(ConversionResult result)
    {
        var query = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("FROM Orders AS o", query, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    private const string Context = """
        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;

            {{code}}
        }

        public class Order
        {
            public int OrderID { get; set; }

            public decimal Amount { get; set; }
        }
        """;

    /// <summary>Inside the context its own set is a set the unit declares: every hand-over is a query, whatever type it goes to.</summary>
    [Theory]
    [InlineData("public IQueryable<Order> All() => Orders;")]
    [InlineData("public IQueryable<Order> All() => this.Orders;")]
    [InlineData("public IQueryable<Order> All() { return Orders; }")]
    [InlineData("public IEnumerable<Order> All() => Orders;")]
    [InlineData("public IQueryable<Order> Open => Orders;")]
    [InlineData("public List<Order> All() => [.. Orders];")]
    [InlineData("public List<Order> All() => new List<Order>(Orders);")]
    [InlineData("public Func<IQueryable<Order>> All => () => Orders;")]
    public void TheContextsOwnSetHandedOverIsAQuery(string code)
    {
        var result = Convert(Unit(Context.Replace("{{code}}", code, StringComparison.Ordinal)));

        AssertTheWholeTable(result);
        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
    }

    /// <summary>
    /// A step of LINQ called in its static form composes the query it is handed, which the reading
    /// does not read: refused naming the call, for a set by name and for Set&lt;T&gt;() alike, written
    /// directly or through a variable. It used to come out as the whole table - a filter dropped,
    /// a count answered with rows.
    /// </summary>
    [Theory]
    [InlineData("public List<Order> Big() => Queryable.Where(Orders, o => o.Amount > 100).ToList();", "Queryable.Where()")]
    [InlineData("public List<Order> Big() => Queryable.Where(Set<Order>(), o => o.Amount > 100).ToList();", "Queryable.Where()")]
    [InlineData("public int Count() => Queryable.Count(Orders);", "Queryable.Count()")]
    [InlineData("public Task<int> Count() => EntityFrameworkQueryableExtensions.CountAsync(Orders);", "EntityFrameworkQueryableExtensions.CountAsync()")]
    [InlineData("public List<Order> All() { var all = Orders; return Enumerable.ToList(all); }", "Enumerable.ToList()")]
    [InlineData("public List<Order> All() { var all = Set<Order>(); return Enumerable.ToList(all); }", "Enumerable.ToList()")]
    public void ASetHandedToAStaticStepIsRefusedNamingIt(string code, string step)
    {
        var result = Convert(Unit(Context.Replace("{{code}}", code, StringComparison.Ordinal)));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains(step, Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit == File).Reason, StringComparison.Ordinal);
    }

    /// <summary>nameof names the set and hands nothing over.</summary>
    [Fact]
    public void NameofIsNoHandOver()
    {
        var result = Convert(Unit(Context.Replace("{{code}}", "public string Table() => nameof(Orders);", StringComparison.Ordinal)));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
    }

    /// <summary>A field of a context the unit declares: its DbSet handed over is a query, and the repository is the code around it.</summary>
    [Theory]
    [InlineData("public IQueryable<Order> All() => _ctx.Orders;")]
    [InlineData("public IEnumerable<Order> All() => this._ctx.Orders;")]
    [InlineData("public int Count() => Count(_ctx.Orders);\n\n    private static int Count(object orders) => 0;")]
    public void ASetOfTheUnitsContextHandedOverIsAQuery(string code)
    {
        var result = Convert(Unit(Context.Replace("{{code}}", "", StringComparison.Ordinal)
            + "\n\npublic class Repo\n{\n    private readonly ShopContext _ctx = null!;\n\n    " + code + "\n}\n"));

        AssertTheWholeTable(result);
        Assert.Contains(NotEntities(result), r => r.Reason.Contains("'Repo'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The context is in another file, so the set is known by its position; the unit states a
    /// query type for the place it goes to - the return type, also of an async member, the type
    /// of a property given it, the parameter of a method or constructor the unit declares.
    /// </summary>
    [Theory]
    [InlineData("public IQueryable<Order> All() => ctx.Orders;")]
    [InlineData("public IQueryable<Order> All() { return ctx.Orders; }")]
    [InlineData("public async Task<IQueryable<Order>> All() { await Task.Yield(); return ctx.Orders; }")]
    [InlineData("public DbSet<Order> All() => ctx.Orders;")]
    [InlineData("public IQueryable<Order> Open => ctx.Orders;")]
    [InlineData("public IQueryable<Order> Open { get; } = ctx.Orders;")]
    [InlineData("public IQueryable<Order> All() { var all = ctx.Orders; return all; }")]
    [InlineData("public int Total() => Sum(ctx.Orders);\n\n    private static int Sum(IQueryable<Order> orders) => 0;")]
    [InlineData("public int Total() => Sum(orders: ctx.Orders);\n\n    private static int Sum(int floor = 0, IQueryable<Order>? orders = null) => 0;")]
    [InlineData("public int Total() => new Runner(ctx.Orders).Run();\n}\n\npublic class Runner(IQueryable<Order> orders)\n{\n    public int Run() => 0;")]
    public void ASetKnownByItsPositionIsAQueryWhereTheUnitStatesAQueryType(string code)
    {
        var result = Convert(Unit("public class Repo(ShopContext ctx)\n{\n    " + code + "\n}\n"));

        AssertTheWholeTable(result);
        Assert.Contains(NotEntities(result), r => r.Reason.Contains("'Repo'", StringComparison.Ordinal));
    }

    /// <summary>A property the constructor gives the set states its type the same way an initializer does (decision 158 reads the two alike).</summary>
    [Fact]
    public void APropertyTheConstructorAssignsStatesItsType()
    {
        var result = Convert(Unit("""
            public class Repo
            {
                public Repo(ShopContext ctx) { Open = ctx.Orders; }

                public IQueryable<Order> Open { get; }
            }
            """));

        AssertTheWholeTable(result);
    }

    /// <summary>
    /// Where the unit states no query type, a member of a name it says nothing of reads the same
    /// as <c>customer.Name</c>: no place, no record, and the class stays an entity - the limit
    /// of decision 114 for a root known by its position.
    /// </summary>
    [Theory]
    [InlineData("public IEnumerable<Order> All() => ctx.Orders;")]
    [InlineData("public object All() => ctx.Orders;")]
    [InlineData("public List<Order> All() => new List<Order>(ctx.Orders);")]
    [InlineData("public Func<object> All => () => ctx.Orders;")]
    [InlineData("public string Name(Customer customer) => customer.Name;")]
    [InlineData("public void Show(Customer customer) => Log(customer.Name);\n\n    private static void Log(string text) { }")]
    public void ARootKnownByItsPositionWithNoQueryTypeIsNoPlace(string code)
    {
        var result = Convert(Unit("public class Repo(ShopContext ctx)\n{\n    public int RepoID { get; set; }\n\n    " + code + "\n}\n"));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Empty(NotEntities(result));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A variable the code assigns more than once and does not continue hands over whichever value
    /// reaches it: the whole table is a query, as the filtered chain beside it is, each numbered by
    /// where it is written.
    /// </summary>
    [Fact]
    public void EveryValueOfAVariableAssignedTwiceIsAQuery()
    {
        var result = Convert(Unit("""
            public class Repo(ShopContext ctx)
            {
                public IQueryable<Order> All(bool big)
                {
                    IQueryable<Order> q = ctx.Orders;
                    if (big) q = ctx.Orders.Where(o => o.Amount > 100);
                    return q;
                }
            }
            """));

        var methods = Artifacts(result, ConversionContentType.CSharpQuery);
        Assert.Equal(2, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(", StringComparison.Ordinal) && !m.Contains("WHERE", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query02(", StringComparison.Ordinal) && m.Contains("100", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A member the unit declares as an IQueryable handed over is refused naming it, as a loop or a chain over it is (decision 114).</summary>
    [Fact]
    public void AMemberDeclaredAsAQueryOfItsOwnIsRefusedNamingIt()
    {
        var result = Convert(Unit("""
            public class CustomerRepository(ShopContext ctx)
            {
                public IQueryable<Customer> Rich => ctx.Customers.Where(c => c.CreditLimit > 2000);
            }

            public class Reports(CustomerRepository repo)
            {
                public IQueryable<Customer> Rich() => repo.Rich;
            }
            """));

        Assert.Contains("2000", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
        Assert.Contains("'repo.Rich'", Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure).Reason, StringComparison.Ordinal);
    }

    /// <summary>A navigation of a type the unit declares, returned, walks objects in memory: no query, and every class stays an entity.</summary>
    [Fact]
    public void ANavigationReturnedIsNoQuery()
    {
        var result = Convert(Unit("""
            public class SalesOrder
            {
                public int SalesOrderID { get; set; }

                public List<OrderLine> Lines { get; set; } = [];
            }

            public class OrderLine
            {
                public int OrderLineID { get; set; }
            }

            public class Totals
            {
                public IEnumerable<OrderLine> Lines(SalesOrder order) => order.Lines;

                public List<OrderLine> Copy(SalesOrder order) => [.. order.Lines];
            }
            """));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Equal(3, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(NotEntities(result));
    }
}
