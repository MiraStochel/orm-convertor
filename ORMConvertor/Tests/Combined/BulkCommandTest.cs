using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.EFCore;
using Tests.NHibernate;

namespace Tests.Combined;

/// <summary>
/// A chain that ends in a bulk command - EF Core's ExecuteDelete and ExecuteUpdate, NHibernate's
/// Delete, Update, UpdateVersioned and InsertInto and the builders of the last two - runs a
/// statement that writes the rows the chain selects (<c>EFCore/EFCoreBulkCommandProviderTest</c>,
/// <c>NHibernate/NHibernateBulkCommandProviderTest</c>), and the tool translates queries that read:
/// the place is refused naming the call, as a SQL statement that is not a SELECT that reads is
/// (decision 108) and EF Core's Database.ExecuteSql… (decision 113), and the class around it is
/// the code around its queries. It used to come out as the SELECT of the rows the command writes,
/// with a loss record saying the call was left out - a delete translated into a read (decision 053).
/// </summary>
public class BulkCommandTest
{
    private const string File = "Repo.cs";

    private const string Entities = """
        public class Order
        {
            public virtual int OrderID { get; set; }

            public virtual decimal Amount { get; set; }
        }

        public class Archived
        {
            public virtual int ArchivedID { get; set; }

            public virtual decimal Amount { get; set; }
        }
        """;

    private static ConversionResult Convert(ORMEnum source, string repository)
        => ConversionHandler.Convert(source, ORMEnum.Dapper,
        [
            new() { ContentType = ConversionContentType.CSharp, Content = repository + "\n\n" + Entities, Name = File },
        ]);

    private static void AssertRefusedNaming(ConversionResult result, string call)
    {
        Assert.Empty(result.Sources.Where(s => s.ContentType == ConversionContentType.SqlQuery));

        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit == File);
        Assert.Contains(call, failure.Reason, StringComparison.Ordinal);
        Assert.Contains("not a SELECT that reads", failure.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains(call, StringComparison.Ordinal));

        Assert.Contains(result.Records, r => r.Reason.Contains("The class 'Repo' hands a query over", StringComparison.Ordinal));
    }

    private static string EFCoreRepository(string code) => $$"""
        public class Repo(ShopContext ctx)
        {
            {{code}}
        }
        """;

    [Theory]
    [InlineData("public int Purge() => ctx.Orders.Where(o => o.Amount < 1).ExecuteDelete();", "ExecuteDelete()")]
    [InlineData("public int Purge() => ctx.Orders.ExecuteDelete();", "ExecuteDelete()")]
    [InlineData("public Task<int> Purge(CancellationToken token) => ctx.Orders.Where(o => o.Amount < 1).ExecuteDeleteAsync(token);", "ExecuteDeleteAsync()")]
    [InlineData("public async Task Purge() { await ctx.Orders.Where(o => o.Amount < 1).ExecuteDeleteAsync(); }", "ExecuteDeleteAsync()")]
    [InlineData("public int Reset() => ctx.Orders.Where(o => o.Amount < 1).ExecuteUpdate(s => s.SetProperty(o => o.Amount, 0m));", "ExecuteUpdate()")]
    [InlineData("public Task<int> Reset() => ctx.Orders.Where(o => o.Amount < 1).ExecuteUpdateAsync(s => s.SetProperty(o => o.Amount, 0m));", "ExecuteUpdateAsync()")]
    [InlineData("public int Purge() { var small = ctx.Orders.Where(o => o.Amount < 1); return small.ExecuteDelete(); }", "ExecuteDelete()")]
    [InlineData("public string Purge() { var deleted = ctx.Orders.Where(o => o.Amount < 1).ExecuteDelete(); return deleted.ToString(); }", "ExecuteDelete()")]
    [InlineData("public int Purge() => EntityFrameworkQueryableExtensions.ExecuteDelete(ctx.Orders.Where(o => o.Amount < 1));", "EntityFrameworkQueryableExtensions.ExecuteDelete()")]
    public void AnEFCoreBulkCommandIsRefusedNamingIt(string code, string call)
        => AssertRefusedNaming(Convert(ORMEnum.EFCore, EFCoreRepository(code)), call);

    public static TheoryData<string> EFCoreCommands => [.. EFCoreBulkCommandProviderTest.Commands()];

    /// <summary>Every bulk command of the pinned version, written as the last step of a chain.</summary>
    [Theory]
    [MemberData(nameof(EFCoreCommands))]
    public void EveryEFCoreBulkCommandIsRefused(string command)
        => AssertRefusedNaming(
            Convert(ORMEnum.EFCore, EFCoreRepository($"public object Run() => ctx.Orders.Where(o => o.Amount < 1).{command}();")),
            command + "()");

    private static string NHibernateRepository(string code) => $$"""
        public class Repo(ISession session)
        {
            {{code}}
        }
        """;

    [Theory]
    [InlineData("public int Purge() => session.Query<Order>().Where(o => o.Amount < 1).Delete();", "Delete()")]
    [InlineData("public Task<int> Purge() => session.Query<Order>().Where(o => o.Amount < 1).DeleteAsync();", "DeleteAsync()")]
    [InlineData("public int Reset() => session.Query<Order>().Where(o => o.Amount < 1).Update(o => new Order { Amount = 0m });", "Update()")]
    [InlineData("public int Reset() => session.Query<Order>().Where(o => o.Amount < 1).UpdateVersioned(o => new Order { Amount = 0m });", "UpdateVersioned()")]
    [InlineData("public int Reset() => session.Query<Order>().Where(o => o.Amount < 1).UpdateBuilder().Set(o => o.Amount, 0m).Update();", "UpdateBuilder()")]
    [InlineData("public int Archive() => session.Query<Order>().Where(o => o.Amount < 1).InsertInto(o => new Archived { ArchivedID = o.OrderID, Amount = o.Amount });", "InsertInto()")]
    [InlineData("public int Archive() => session.Query<Order>().Where(o => o.Amount < 1).InsertBuilder().Into<Archived>().Value(a => a.ArchivedID, o => o.OrderID).Insert();", "InsertBuilder()")]
    [InlineData("public int Purge() { var small = session.Query<Order>().Where(o => o.Amount < 1); return small.Delete(); }", "Delete()")]
    [InlineData("public string Purge() { var deleted = session.Query<Order>().Where(o => o.Amount < 1).Delete(); return deleted.ToString(); }", "Delete()")]
    [InlineData("public int Reset() { var builder = session.Query<Order>().Where(o => o.Amount < 1).UpdateBuilder(); return builder.Set(o => o.Amount, 0m).Update(); }", "UpdateBuilder()")]
    [InlineData("public int Purge() => DmlExtensionMethods.Delete(session.Query<Order>().Where(o => o.Amount < 1));", "DmlExtensionMethods.Delete()")]
    public void AnNHibernateBulkCommandIsRefusedNamingIt(string code, string call)
        => AssertRefusedNaming(Convert(ORMEnum.NHibernate, NHibernateRepository(code)), call);

    public static TheoryData<string> NHibernateCommands => [.. NHibernateBulkCommandProviderTest.Commands()];

    /// <summary>Every bulk command of the pinned version, written as the last step of a chain.</summary>
    [Theory]
    [MemberData(nameof(NHibernateCommands))]
    public void EveryNHibernateBulkCommandIsRefused(string command)
        => AssertRefusedNaming(
            Convert(ORMEnum.NHibernate, NHibernateRepository($"public object Run() => session.Query<Order>().Where(o => o.Amount < 1).{command}();")),
            command + "()");
}
