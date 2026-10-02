using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A root EF Core recognizes by its position - a member of a name, <c>x.M</c> - is a query
/// root only where the unit does not state otherwise (decision 114). Whether
/// <c>x.M.Where(f)</c> is a query is decided in C# by the static type of <c>x.M</c>, and the
/// unit states it where it declares the type of x and the member on it, or where x holds what
/// a run query returned: then a member that is no DbSet is a navigation over objects in
/// memory, no query, and the class around it stays what it was. Where the unit states nothing,
/// the position holds; the conversion's mapping may then refuse the place, never unmake it.
/// It used to come out as a query over a table named after the navigation.
/// </summary>
public class LoadedEntityNavigationTest
{
    private const string File = "Sales.cs";

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [.. units]);

    private static ConversionSource Unit(string content, string name = File)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static List<ConversionRecord> NotEntities(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.Contains("not as an entity", StringComparison.Ordinal)
            || r.Reason.Contains("is the EF Core context", StringComparison.Ordinal))];

    private const string Entities = """
        public class SalesOrder
        {
            public int SalesOrderID { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }

        public class OrderLine
        {
            public int OrderLineID { get; set; }

            public int SalesOrderID { get; set; }

            public decimal Amount { get; set; }

            public SalesOrder Order { get; set; } = null!;
        }
        """;

    /// <summary>
    /// The unit declares the entity and its collection, so a chain over the collection of a
    /// parameter, of a property or of a property through this walks objects in memory: no query,
    /// no record, and every class stays an entity - the one holding the walk included.
    /// </summary>
    [Theory]
    [InlineData("public List<OrderLine> Big(SalesOrder order) => order.Lines.Where(l => l.Amount > 100).ToList();")]
    [InlineData("public int Siblings() => Order.Lines.Count(l => l.Amount > 100);")]
    [InlineData("public int Siblings() => this.Order.Lines.Where(l => l.Amount > 100).Count();")]
    public void AMemberOfATypeTheUnitDeclaresIsANavigationAndNoQuery(string walk)
    {
        var result = Convert(Unit(Entities + "\n\npublic class Totals\n{\n    public SalesOrder Order { get; set; } = null!;\n\n    " + walk + "\n}\n"));

        Assert.Equal(3, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Empty(NotEntities(result));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A local that holds what a run query returned holds objects in memory, whatever type the
    /// unit gives it - here none, since the unit declares neither the context nor the entity -
    /// and whatever the query ran over: the load is a query, the walk over its result is not.
    /// </summary>
    [Theory]
    [InlineData("public List<OrderLine> Big() { var order = ctx.SalesOrders.First(o => o.SalesOrderID == 7); return order.Lines.Where(l => l.Amount > 100).ToList(); }")]
    [InlineData("public async Task<List<OrderLine>> Big() { var order = await ctx.SalesOrders.FirstAsync(o => o.SalesOrderID == 7); return order.Lines.Where(l => l.Amount > 100).ToList(); }")]
    [InlineData("public async Task<List<OrderLine>> Big() { var order = await ctx.SalesOrders.SingleAsync(o => o.SalesOrderID == 7).ConfigureAwait(false); return order.Lines.Where(l => l.Amount > 100).ToList(); }")]
    [InlineData("public List<OrderLine> Big() { var order = ctx.SalesOrders.Find(7); return order.Lines.Where(l => l.Amount > 100).ToList(); }")]
    [InlineData("public List<OrderLine> Big(List<SalesOrder> orders) { var order = orders.First(); return order.Lines.Where(l => l.Amount > 100).ToList(); }")]
    public void AVariableHoldingWhatARunQueryReturnedIsNoQueryRoot(string method)
    {
        var result = Convert(Unit("public class Totals(ShopContext ctx)\n{\n    " + method + "\n}\n"));

        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("Lines", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("Lines", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Query == "Query02");
    }

    private const string ContextFile = """
        public class ShopContext : DbContext
        {
            public DbSet<Customer> Customers { get; set; } = null!;

            public List<string> Tags { get; set; } = [];
        }

        public class Customer
        {
            public int CustomerID { get; set; }

            public string CustomerName { get; set; } = "";

            public decimal CreditLimit { get; set; }
        }
        """;

    /// <summary>
    /// A field whose type is the unit's context reads its DbSet as a root, written bare or
    /// through this - the latter used not to be read at all -, and a member of the context that
    /// is no DbSet is a list in memory.
    /// </summary>
    [Fact]
    public void AFieldOfTheUnitsContextIsReadBareAndThroughThis()
    {
        const string reports = """

            public class Reports
            {
                private readonly ShopContext _ctx = null!;

                public List<Customer> Rich() => _ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();

                public List<Customer> Ordered() => this._ctx.Customers.OrderBy(c => c.CustomerName).ToList();

                public List<string> Short() => _ctx.Tags.Where(t => t.Length < 3).ToList();
            }
            """;

        var result = Convert(Unit(ContextFile + reports));

        var queries = Artifacts(result, ConversionContentType.SqlQuery);
        Assert.Equal(2, queries.Count);
        Assert.Contains(queries, q => q.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Contains("ORDER BY", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("Tags", StringComparison.Ordinal));

        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains(NotEntities(result), r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("'Reports'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A field through this whose type the unit does not declare is read by position, as the bare field is.</summary>
    [Fact]
    public void AFieldThroughThisOfATypeTheUnitDoesNotDeclareIsReadByPosition()
    {
        const string file = """
            public class Reports
            {
                private readonly ShopContext _ctx = null!;

                public List<Customer> Rich() => this._ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();
            }
            """;

        var result = Convert(Unit(file));

        Assert.Contains("2000", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
        Assert.Empty(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("'Reports'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member the unit declares as an IQueryable is a query composed through the member: the
    /// provider gets a tree that starts with what the member returns, which the reading does not
    /// follow. Refused naming the member, where it used to come out as a query over a table
    /// named after it; the query the member returns is read on its own.
    /// </summary>
    [Fact]
    public void AMemberDeclaredAsAQueryOfItsOwnIsRefusedNamingIt()
    {
        const string file = """
            public class CustomerRepository(ShopContext ctx)
            {
                public IQueryable<Customer> Rich => ctx.Customers.Where(c => c.CreditLimit > 2000);
            }

            public class Reports(CustomerRepository repo)
            {
                public List<Customer> Named() => repo.Rich.OrderBy(c => c.CustomerName).ToList();
            }
            """;

        var result = Convert(Unit(file));

        Assert.Contains("2000", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);

        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Query02", failure.Query);
        Assert.Contains("'repo.Rich'", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("IQueryable<Customer>", failure.Reason, StringComparison.Ordinal);
    }

    private const string Walk = """
        public class Totals
        {
            public List<OrderLine> Big(SalesOrder order) => order.Lines.Where(l => l.Amount > 100).ToList();
        }
        """;

    /// <summary>
    /// The unit names the type of the parameter without declaring it, so it states nothing the
    /// two passes could tell a navigation by, and the position holds: the place is a place, and
    /// the class around it is the code around a query. The conversion maps the type as an
    /// entity, though, and an entity has no DbSet: the place is refused by name rather than read
    /// into a query over a table named after the navigation.
    /// </summary>
    [Fact]
    public void ATypeTheConversionMapsAsAnEntityRefusesThePlace()
    {
        var result = Convert(Unit(Entities, "Entities.cs"), Unit(Walk));

        Assert.Equal(2, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));

        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(File, failure.Unit);
        Assert.Contains("'order.Lines'", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("'SalesOrder'", failure.Reason, StringComparison.Ordinal);

        Assert.Contains("'Totals'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Without the mapping the same unit states nothing either way, and the position reads the
    /// navigation as a query - the limit decision 114 states, kept here so that a change to it
    /// is a decision rather than an accident.
    /// </summary>
    [Fact]
    public void WithoutTheMappingTheSameUnitIsReadByPosition()
    {
        var result = Convert(Unit(Walk));

        Assert.Contains("Lines", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
    }
}
