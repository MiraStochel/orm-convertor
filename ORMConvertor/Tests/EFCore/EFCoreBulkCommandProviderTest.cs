using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.EFCore;

/// <summary>
/// What the refusal of a bulk command over a chain rests on (<c>EFCoreLinqQueryParser.ProviderCommandOf</c>),
/// measured against EF Core 10 without a database - the opening of the connection suppressed and
/// the command taken where it would be sent: ExecuteDelete and ExecuteUpdate, and their async
/// forms, send a DELETE and an UPDATE of the rows the chain selects, as a command that reads no
/// rows; and they are the only steps of EF Core's query classes over a query that answer a number
/// of rows and are no operator of System.Linq. Should a new version add one, this fails here.
/// </summary>
public class EFCoreBulkCommandProviderTest
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

    /// <summary>
    /// The steps over a query in the static classes of EF Core's query API that answer an int,
    /// which is the number of rows a command changed, leaving out the async forms of System.Linq's
    /// operators (CountAsync, SumAsync), whose int is a value of the rows.
    /// </summary>
    public static IReadOnlyList<string> Commands()
    {
        var operators = typeof(Queryable).GetMethods().Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. new[] { typeof(EntityFrameworkQueryableExtensions), typeof(RelationalQueryableExtensions) }
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(method => method.GetParameters() is [{ ParameterType: { IsGenericType: true } first }, ..]
                                 && first.GetGenericTypeDefinition() == typeof(IQueryable<>))
                .Where(method => method.ReturnType == typeof(int) || method.ReturnType == typeof(Task<int>))
                .Where(method => !operators.Contains(method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^"Async".Length] : method.Name))
                .Select(method => method.Name)
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];
    }

    [Fact]
    public void TheCommandsAreTheStepsThatAnswerANumberOfRows()
        => Assert.Equal(["ExecuteDelete", "ExecuteDeleteAsync", "ExecuteUpdate", "ExecuteUpdateAsync"], Commands());

    [Fact]
    public void ExecuteDeleteSendsADelete()
    {
        using var context = Context();

        var sent = Assert.Throws<Sent>(() => context.Orders.Where(o => o.Amount < 1).ExecuteDelete()).Message;

        Assert.StartsWith("DELETE FROM [o]", sent, StringComparison.Ordinal);
        Assert.Contains("WHERE [o].[Amount] < 1.0", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteDeleteAsyncSendsADelete()
    {
        await using var context = Context();

        var sent = (await Assert.ThrowsAsync<Sent>(() => context.Orders.Where(o => o.Amount < 1).ExecuteDeleteAsync())).Message;

        Assert.StartsWith("DELETE FROM [o]", sent, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecuteUpdateSendsAnUpdate()
    {
        using var context = Context();

        var sent = Assert.Throws<Sent>(() => context.Orders.Where(o => o.Amount < 1).ExecuteUpdate(s => s.SetProperty(o => o.Amount, 0m))).Message;

        Assert.StartsWith("UPDATE [o]", sent, StringComparison.Ordinal);
        Assert.Contains("WHERE [o].[Amount] < 1.0", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteUpdateAsyncSendsAnUpdate()
    {
        await using var context = Context();

        var sent = (await Assert.ThrowsAsync<Sent>(() => context.Orders.Where(o => o.Amount < 1).ExecuteUpdateAsync(s => s.SetProperty(o => o.Amount, 0m)))).Message;

        Assert.StartsWith("UPDATE [o]", sent, StringComparison.Ordinal);
    }

    private static ShopContext Context() => new(new DbContextOptionsBuilder()
        .UseSqlServer("Server=unreachable;Database=None")
        .AddInterceptors(new Unopened(), new Taken())
        .EnableServiceProviderCaching(false)
        .Options);

    /// <summary>The text of the command EF Core was about to send.</summary>
    private sealed class Sent(string sql) : Exception(sql);

    private sealed class Unopened : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
            => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    /// <summary>Takes a command that reads no rows; one that reads rows reaches the suppressed connection and fails otherwise.</summary>
    private sealed class Taken : DbCommandInterceptor
    {
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
            => throw new Sent(command.CommandText);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new Sent(command.CommandText);
    }
}
