using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Model;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// Fourth verification level of decision 016 against EF Core: the generated entities are
/// used to store rows and load them back with the same identity. Like
/// <see cref="DapperToEFCoreVerificationTest"/> it runs the F6 pipeline against the
/// fixture schema, and it writes inside its own rolled-back transaction over
/// <c>OpenConnection()</c>, the boundary the fixture prepared for exactly this. The
/// entity types exist only at runtime, so <c>dynamic</c> stands in for the compile-time
/// reference a consumer project would have.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperToEFCorePersistenceTest(TestSchemaFixture fixture)
{
    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences);

    [Fact]
    public void StoredCustomerLoadsBackWithItsIdentityAndItsOrders()
    {
        fixture.SkipIfUnavailable();

        var entities = Assembly.Load(CompileEntities(
            DapperSourceEntities.Convert(ORMEnum.EFCore, fixture.CatalogReader).Sources));
        var customerType = entities.GetType("DapperEntities.Customer")!;
        var orderType = entities.GetType("DapperEntities.Order")!;

        using var connection = fixture.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var context = EFCoreAcceptance.OpenContext(entities, connection);
        context.Database.UseTransaction(transaction);

        var placedAt = new DateTime(2026, 8, 24, 10, 30, 0);

        dynamic customer = Activator.CreateInstance(customerType)!;
        customer.Name = "Persisted customer";

        int[] orderIds = [1, 2];
        foreach (var orderId in orderIds)
        {
            dynamic order = Activator.CreateInstance(orderType)!;
            order.CompanyId = 7;
            order.OrderId = orderId;
            order.OrderDate = new DateTime(2026, 8, 24);
            order.PlacedAt = placedAt;
            order.IsCancelled = false;
            // Reached over the navigation, so SaveChanges has to write the foreign key
            // from the relationship the catalog supplied, not from a value set by hand.
            customer.Orders.Add(order);
        }

        context.Add((object)customer);
        context.SaveChanges();

        // The database generated the identity and EF Core read it back into the key.
        var customerId = (int)customer.CustomerId;
        Assert.NotEqual(0, customerId);

        context.ChangeTracker.Clear();

        var reloaded = context.Find(customerType, customerId);
        Assert.NotNull(reloaded);
        dynamic loadedCustomer = reloaded!;
        Assert.Equal(customerId, (int)loadedCustomer.CustomerId);
        Assert.Equal("Persisted customer", (string)loadedCustomer.Name);

        // No lazy loading here - the collection is loaded explicitly, the way a consumer
        // of the generated entities would.
        context.Entry(reloaded!).Collection("Orders").Load();
        Assert.Equal(2, (int)loadedCustomer.Orders.Count);

        var reloadedOrder = context.Find(orderType, 7, 2);
        Assert.NotNull(reloadedOrder);
        dynamic loadedOrder = reloadedOrder!;
        Assert.Equal(customerId, (int)loadedOrder.CustomerId);
        Assert.Equal(placedAt, (DateTime)loadedOrder.PlacedAt);

        transaction.Rollback();
    }
}
