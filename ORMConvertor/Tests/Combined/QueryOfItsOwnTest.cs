using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A chain that starts at a query the unit declares as its own - a parameter, a field or a
/// property declared as an IQueryable, or a local declared so whose value the reading does not
/// follow - gives the provider a tree that begins with what the caller passes or the name holds,
/// which the text does not state: refused naming it, as decision 114 refuses a member of a name
/// declared so. Every one of them used to vanish without a word. Enumerated as it stands it runs
/// that query and is refused too; returned or passed on unchanged it is no place of the unit, and
/// a name declared otherwise before the steps of LINQ is objects in memory.
/// </summary>
public class QueryOfItsOwnTest
{
    private const string File = "Queries.cs";

    private const string Order = """
        public class Order
        {
            public int OrderID { get; set; }

            public decimal Amount { get; set; }
        }


        """;

    private static ConversionResult Convert(string content, ORMEnum source = ORMEnum.EFCore)
        => ConversionHandler.Convert(source, ORMEnum.Dapper, [new ConversionSource { ContentType = ConversionContentType.CSharp, Content = Order + content, Name = File }]);

    private static string Static(string code) => "public static class Queries\n{\n    " + code + "\n}\n";

    private static List<string> Queries(ConversionResult result)
        => [.. result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery).Select(s => s.Content)];

    private static List<ConversionRecord> Failures(ConversionResult result)
        => [.. result.Records.Where(r => r.Kind == ConversionRecordKind.Failure)];

    [Theory]
    [InlineData("public static IQueryable<Order> Big(this IQueryable<Order> q) => q.Where(o => o.Amount > 100);", "'q', a parameter")]
    [InlineData("public static Task<List<Order>> Page(IQueryable<Order> source, int n) => source.OrderBy(o => o.OrderID).Skip(n).ToListAsync();", "'source', a parameter")]
    [InlineData("public static int Count(IOrderedQueryable<Order> sorted) => sorted.Count();", "'sorted', a parameter")]
    [InlineData("public static int Count(IQueryable? untyped) => untyped.Cast<Order>().Count();", "'untyped', a parameter")]
    [InlineData("public static Func<IQueryable<Order>, int> Count = (IQueryable<Order> q) => q.Count();", "'q', a parameter")]
    [InlineData("public static List<Order> Big(IQueryable<Order> q) { var big = q.Where(o => o.Amount > 100); return big.ToList(); }", "'q', a parameter")]
    [InlineData("public static List<Order> Big(IQueryable<Order> q) { IQueryable<Order> all = q; return all.Where(o => o.Amount > 100).ToList(); }", "'q', a parameter")]
    [InlineData("public static List<Order> All(IQueryable<Order> q) { var all = new List<Order>(); foreach (var o in q) all.Add(o); return all; }", "'q', a parameter")]
    [InlineData("public static async Task<int> All(IQueryable<Order> q) { var n = 0; await foreach (var o in q.AsAsyncEnumerable()) n++; return n; }", "'q', a parameter")]
    [InlineData("public static List<Order> All(IQueryable<Order> q) => [.. q];", "'q', a parameter")]
    [InlineData("public static List<Order> Big(IQueryable<Order> q) { IQueryable<Order> local = Source(q); return local.Where(o => o.Amount > 100).ToList(); }\n\n    private static IQueryable<Order> Source(IQueryable<Order> q) => q;", "'local', a local")]
    [InlineData("public static List<Order> Big(Repo repo) { IQueryable<Order> local = repo.Query(); return local.Where(o => o.Amount > 100).ToList(); }", "'local', a local")]
    public void AChainOverAQueryOfItsOwnIsRefusedNamingIt(string code, string named)
    {
        var result = Convert(Static(code));

        Assert.Empty(Queries(result));
        Assert.Contains(named, Assert.Single(Failures(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>A member declared as an IQueryable, bare or through this, and the parameter a field is given, refused naming each; the class around is code, not an entity.</summary>
    [Theory]
    [InlineData("public List<Order> Big() => Recent.Where(o => o.Amount > 100).ToList();", "'Recent', a member")]
    [InlineData("public List<Order> Big() => this.Recent.Where(o => o.Amount > 100).ToList();", "'this.Recent', a member")]
    [InlineData("public List<Order> All() => [.. Recent];", "'Recent', a member")]
    [InlineData("public List<Order> Big() => _unset.Where(o => o.Amount > 100).ToList();", "'_unset', a member")]
    [InlineData("public List<Order> Big() => _orders.Where(o => o.Amount > 100).ToList();", "'injected', a parameter")]
    [InlineData("public int Count() => injected.Count();", "'injected', a parameter")]
    public void AChainOverAMemberOrAConstructorParameterIsRefusedNamingIt(string code, string named)
    {
        var result = Convert($$"""
            public class Reports(IQueryable<Order> injected)
            {
                private readonly IQueryable<Order> _orders = injected;

                private readonly IQueryable<Order> _unset;

                private IQueryable<Order> Recent { get; } = injected;

                {{code}}
            }
            """);

        Assert.Empty(Queries(result));
        Assert.Contains(named, Assert.Single(Failures(result)).Reason, StringComparison.Ordinal);
        Assert.Contains(result.Records, r => r.Reason.Contains("'Reports'", StringComparison.Ordinal) && r.Reason.Contains("not as an entity", StringComparison.Ordinal));
    }

    /// <summary>
    /// What states no query of the unit's own: the query handed back or on unchanged, a list made
    /// a query in memory, an IEnumerable, the value of a call kept in a local the unit does not
    /// declare as a query.
    /// </summary>
    [Theory]
    [InlineData("public static IQueryable<Order> Same(IQueryable<Order> q) => q;")]
    [InlineData("public static int Count(IQueryable<Order> q) => Counted(q);\n\n    private static int Counted(IQueryable<Order> q) => 0;")]
    [InlineData("public static List<Order> Big(List<Order> orders) { IQueryable<Order> q = orders.AsQueryable(); return q.Where(o => o.Amount > 100).ToList(); }")]
    [InlineData("public static List<Order> Big(IEnumerable<Order> orders) => orders.Where(o => o.Amount > 100).ToList();")]
    [InlineData("public static List<Order> Big(Repo repo) { var q = repo.Query(); return q.Where(o => o.Amount > 100).ToList(); }")]
    public void ANameThatStatesNoQueryOfItsOwnIsNoPlace(string code)
    {
        var result = Convert(Static(code));

        Assert.Empty(Queries(result));
        Assert.Empty(Failures(result));
    }

    /// <summary>
    /// A query of its own kept in a variable the code assigns again, or gives a value a condition
    /// chooses, and continues is refused once for every value kept there, as a query composed at
    /// run time is - not once more for the continuation.
    /// </summary>
    [Theory]
    [InlineData("IQueryable<Order> q = source; if (big) q = q.Where(o => o.Amount > 100); return q.ToList();", 1)]
    [InlineData("IQueryable<Order> q = big ? source.Where(o => o.Amount > 100) : source; return q.ToList();", 2)]
    public void AVariableWhoseValueTheTextDoesNotFixIsRefusedForEachValue(string body, int refusals)
    {
        var result = Convert(Static("public static List<Order> Filter(IQueryable<Order> source, bool big) { " + body + " }"));

        Assert.Empty(Queries(result));
        Assert.Equal(refusals, Failures(result).Count);
        Assert.All(Failures(result), f => Assert.Contains("'source', a parameter", f.Reason, StringComparison.Ordinal));
    }

    /// <summary>Beside a query over a root, the refused one keeps its number and refuses only itself.</summary>
    [Fact]
    public void TheRefusedPlaceRefusesOnlyItself()
    {
        var result = Convert("""
            public class Repo(ShopContext ctx)
            {
                public List<Order> Rich() => ctx.Orders.Where(o => o.Amount > 1000).ToList();

                public List<Order> Big(IQueryable<Order> q) => q.Where(o => o.Amount > 100).ToList();
            }
            """);

        Assert.Contains("1000", Assert.Single(Queries(result)), StringComparison.Ordinal);
        Assert.Equal("Query02", Assert.Single(Failures(result)).Query);
    }

    /// <summary>The reading is the shared one of LINQ, so a source of NHibernate refuses the same chain.</summary>
    [Fact]
    public void NHibernateRefusesTheSameChain()
    {
        var result = Convert(Static("public static IQueryable<Order> Big(this IQueryable<Order> q) => q.Where(o => o.Amount > 100);"), ORMEnum.NHibernate);

        Assert.Empty(Queries(result));
        Assert.Contains("'q', a parameter", Assert.Single(Failures(result)).Reason, StringComparison.Ordinal);
    }
}
