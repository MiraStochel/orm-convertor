using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A root written as a name and enumerated by a foreach - <c>foreach (var o in ctx.Orders)</c>,
/// awaited or not, the context's own set inside it, a variable holding either - sends the
/// provider a query over the whole table, as the same root run by ToList does (decisions 109
/// and 114), and is a place where the unit hands a query over. The search for places used to
/// take calls alone, so the query vanished without a word and the class around it, with no
/// other hand-over in it, came out as an entity. What the unit states about the name decides
/// as it does for a chain: the enumeration of a navigation stays no query.
/// </summary>
public class ForEachEnumerationTest
{
    private const string File = "Report.cs";

    private static ConversionResult Convert(params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [.. units]);

    private static ConversionSource Unit(string content, string name = File)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static List<ConversionRecord> NotEntities(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.Contains("not as an entity", StringComparison.Ordinal)
            || r.Reason.Contains("is the EF Core context", StringComparison.Ordinal))];

    private static string Service(string loop) => $$"""
        public class Report
        {
            private readonly ShopContext ctx = null!;

            public async Task<decimal> Total()
            {
                decimal total = 0;
                {{loop}}
                return total;
            }
        }
        """;

    /// <summary>
    /// The context is declared in another file, so the position holds (decision 114): the loop
    /// is the query over the whole table, and the service around it is the code around a query.
    /// </summary>
    [Theory]
    [InlineData("foreach (var o in ctx.Orders) { total += o.Amount; }")]
    [InlineData("await foreach (var o in ctx.Orders) { total += o.Amount; }")]
    [InlineData("foreach (var o in this.ctx.Orders) { total += o.Amount; }")]
    [InlineData("foreach (var o in (ctx.Orders)) { total += o.Amount; }")]
    [InlineData("var all = ctx.Orders; foreach (var o in all) { total += o.Amount; }")]
    [InlineData("await foreach (var o in ctx.Orders.WithCancellation(default)) { total += o.Amount; }")]
    [InlineData("await foreach (var o in ctx.Orders.ConfigureAwait(false)) { total += o.Amount; }")]
    [InlineData("var all = ctx.Orders; await foreach (var o in all.WithCancellation(default).ConfigureAwait(false)) { total += o.Amount; }")]
    public void AnEnumeratedRootIsAQueryAndTheClassAroundItIsNoEntity(string loop)
    {
        var result = Convert(Unit(Service(loop)));

        var query = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("FROM Orders AS o", query, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);

        Assert.Empty(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("'Report'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    private const string Context = """
        public class ShopContext : DbContext
        {
            public DbSet<Order> Orders { get; set; } = null!;

            public List<string> Tags { get; set; } = [];

            {{code}}
        }

        public class Order
        {
            public int OrderID { get; set; }

            public decimal Amount { get; set; }
        }
        """;

    /// <summary>Inside the context its own set is the root by bare name and through this (decision 111).</summary>
    [Theory]
    [InlineData("public decimal Total() { decimal total = 0; foreach (var o in Orders) { total += o.Amount; } return total; }")]
    [InlineData("public decimal Total() { decimal total = 0; foreach (var o in this.Orders) { total += o.Amount; } return total; }")]
    public void TheContextsOwnSetEnumeratedIsAQuery(string code)
    {
        var result = Convert(Unit(Context.Replace("{{code}}", code, StringComparison.Ordinal)));

        Assert.Contains("FROM Orders AS o", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A field of the unit's own context: a DbSet member enumerated is the query, a member of
    /// any other type a list in memory (decision 114, answer 3).
    /// </summary>
    [Fact]
    public void TheDeclaredTypeOfTheMemberDecides()
    {
        const string reports = """

            public class Reports
            {
                private readonly ShopContext _ctx = null!;

                public decimal Total() { decimal total = 0; foreach (var o in _ctx.Orders) { total += o.Amount; } return total; }

                public int Short() { var n = 0; foreach (var t in _ctx.Tags) { n += t.Length; } return n; }
            }
            """;

        var result = Convert(Unit(Context.Replace("{{code}}", "", StringComparison.Ordinal) + reports));

        Assert.Contains("FROM Orders AS o", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("Tags", StringComparison.Ordinal));
        Assert.Contains(NotEntities(result), r => r.Reason.Contains("'Reports'", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// The loop is a place like any other and takes its number from its position in the text; a
    /// variable enumerated twice holds one root, which is one place, as a chain a variable holds
    /// is, and a root that is a call is met once, as the chain it is.
    /// </summary>
    [Fact]
    public void TheLoopIsNumberedByItsPosition()
    {
        var result = Convert(Unit("""
            public class Report(ShopContext ctx)
            {
                public decimal Total()
                {
                    decimal total = 0;
                    var all = ctx.Orders;
                    foreach (var o in all) { total += o.Amount; }
                    foreach (var o in all) { total -= o.Amount; }
                    return total + all.Where(o => o.Amount > 100).ToList().Count;
                }

                public decimal Again()
                {
                    decimal total = 0;
                    var set = ctx.Set<Order>();
                    foreach (var o in set) { total += o.Amount; }
                    return total;
                }
            }
            """));

        var methods = Artifacts(result, ConversionContentType.CSharpQuery);
        Assert.Equal(3, methods.Count);
        Assert.Contains(methods, m => m.Contains("Query01(", StringComparison.Ordinal) && !m.Contains("WHERE", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query02(", StringComparison.Ordinal) && m.Contains("100", StringComparison.Ordinal));
        Assert.Contains(methods, m => m.Contains("Query03(", StringComparison.Ordinal) && !m.Contains("WHERE", StringComparison.Ordinal));
    }

    private const string Entities = """
        public class SalesOrder
        {
            public int SalesOrderID { get; set; }

            public List<OrderLine> Lines { get; set; } = [];

            public decimal Sum() { decimal total = 0; foreach (var l in Lines) { total += l.Amount; } return total; }

            public decimal SumThis() { decimal total = 0; foreach (var l in this.Lines) { total += l.Amount; } return total; }
        }

        public class OrderLine
        {
            public int OrderLineID { get; set; }

            public int SalesOrderID { get; set; }

            public decimal Amount { get; set; }
        }
        """;

    /// <summary>
    /// The enumeration of a navigation walks objects in memory: of an element variable, of a
    /// local holding what a run query returned, of a parameter whose type the unit declares, of
    /// the own instance. No query, no record, and every class stays an entity.
    /// </summary>
    [Theory]
    [InlineData("public decimal Sum(SalesOrder order) { decimal total = 0; foreach (var l in order.Lines) { total += l.Amount; } return total; }")]
    [InlineData("public decimal Sum(List<SalesOrder> orders) { decimal total = 0; foreach (var o in orders) { foreach (var l in o.Lines) { total += l.Amount; } } return total; }")]
    [InlineData("public decimal Sum(List<SalesOrder> orders) => orders.Sum(o => { decimal total = 0; foreach (var l in o.Lines) { total += l.Amount; } return total; });")]
    public void TheEnumerationOfANavigationIsNoQuery(string walk)
    {
        var result = Convert(Unit(Entities + "\n\npublic class Totals\n{\n    " + walk + "\n}\n"));

        Assert.Equal(3, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Empty(NotEntities(result));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A local holding what a run query returned holds objects in memory: the load is a query, the loop over its navigation is not.</summary>
    [Fact]
    public void TheNavigationOfALoadedRowEnumeratedIsNoQuery()
    {
        var result = Convert(Unit("""
            public class Totals(ShopContext ctx)
            {
                public decimal Sum()
                {
                    var order = ctx.SalesOrders.First(o => o.SalesOrderID == 7);
                    decimal total = 0;
                    foreach (var l in order.Lines) { total += l.Amount; }
                    return total;
                }
            }
            """));

        Assert.Contains("SalesOrders", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Sources, s => s.Content.Contains("Lines", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Query == "Query02");
    }

    /// <summary>
    /// A member the unit declares as an IQueryable is a query composed through the member, and
    /// a member of a type the conversion maps as an entity a navigation (decision 114): either
    /// enumerated is refused by name, as the chain over it is.
    /// </summary>
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
                public int Count() { var n = 0; foreach (var c in repo.Rich) { n++; } return n; }
            }
            """));

        Assert.Contains("2000", Assert.Single(Artifacts(result, ConversionContentType.SqlQuery)), StringComparison.Ordinal);

        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Query02", failure.Query);
        Assert.Contains("'repo.Rich'", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeTheConversionMapsAsAnEntityRefusesTheLoop()
    {
        const string walk = """
            public class Totals
            {
                public decimal Sum(SalesOrder order) { decimal total = 0; foreach (var l in order.Lines) { total += l.Amount; } return total; }
            }
            """;

        var result = Convert(Unit(Entities, "Entities.cs"), Unit(walk));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));

        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(File, failure.Unit);
        Assert.Contains("'order.Lines'", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("'SalesOrder'", failure.Reason, StringComparison.Ordinal);
    }
}
