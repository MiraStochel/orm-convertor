using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.EFCore;

/// <summary>
/// What the reading of a compiled query rests on (<c>EFCoreLinqQueryParser.IsCompiledQuery</c>),
/// measured against EF Core 10 without a database - the opening of the connection suppressed and
/// the command taken where it would be sent: <c>EF.CompileQuery</c> and <c>EF.CompileAsyncQuery</c>
/// send the query their lambda states over the context they get as the first parameter, the same
/// SQL as the chain written over a context, with the other parameters of the lambda bound as the
/// parameters of the query.
/// </summary>
public class EFCoreCompiledQueryProviderTest
{
    public class Order
    {
        [Key]
        public int OrderId { get; set; }

        public decimal Amount { get; set; }
    }

    public class ShopContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Order> Orders { get; set; } = null!;
    }

    [Fact]
    public void CompileQuerySendsTheChainOverTheContext()
    {
        var compiled = EF.CompileQuery((ShopContext c, decimal min) => c.Orders.Where(o => o.Amount > min));
        using var context = Context();

        var sent = SentBy(() => compiled(context, 100m).ToList());

        var min = 100m;
        var written = context.Orders.Where(o => o.Amount > min).ToQueryString();
        Assert.EndsWith(sent, written, StringComparison.Ordinal);
        Assert.Contains("FROM [Orders] AS [o]", sent, StringComparison.Ordinal);
        Assert.Contains("WHERE [o].[Amount] > @min", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompileAsyncQuerySendsTheChainOverTheContext()
    {
        var compiled = EF.CompileAsyncQuery((ShopContext c, int id) => c.Orders.FirstOrDefault(o => o.OrderId == id));
        await using var context = Context();

        var sent = (await Assert.ThrowsAsync<Sent>(() => compiled(context, 7))).Message;

        Assert.Contains("SELECT TOP(1)", sent, StringComparison.Ordinal);
        Assert.Contains("FROM [Orders] AS [o]", sent, StringComparison.Ordinal);
        Assert.Contains("WHERE [o].[OrderId] = @id", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSetAloneIsTheWholeTable()
    {
        var compiled = EF.CompileQuery((ShopContext c) => c.Orders);
        using var context = Context();

        var sent = SentBy(() => compiled(context).ToList());

        Assert.Contains("FROM [Orders] AS [o]", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", sent, StringComparison.Ordinal);
    }

    private static ShopContext Context() => new(new DbContextOptionsBuilder()
        .UseSqlServer("Server=unreachable;Database=None")
        .AddInterceptors(new Unopened(), new Taken())
        .EnableServiceProviderCaching(false)
        .Options);

    private static string SentBy(Action run) => Assert.Throws<Sent>(run).Message;

    /// <summary>The text of the command EF Core was about to send.</summary>
    private sealed class Sent(string sql) : Exception(sql);

    private sealed class Unopened : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class Taken : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
            => throw new Sent(command.CommandText);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
            => throw new Sent(command.CommandText);
    }
}
