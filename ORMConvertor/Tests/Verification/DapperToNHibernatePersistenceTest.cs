using System.Reflection;
using Model;
using NHibernate;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// Fourth verification level of decision 016: the generated entity and its mapping are
/// actually used to store rows and load them back with the same identity. The scenario
/// continues the F6 pipeline of <see cref="DapperToNHibernateVerificationTest"/> - the
/// artifacts map onto the fixture schema, the only tables the tests own - and follows the
/// fixture's rule for writing tests: its own transaction over <c>OpenConnection()</c>,
/// rolled back at the end. The entity types exist only at runtime, so <c>dynamic</c>
/// stands in for the compile-time reference a consumer project would have.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperToNHibernatePersistenceTest(TestSchemaFixture fixture)
{
    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

    private static IEnumerable<string> Mappings(IEnumerable<ConversionSource> outputs)
        => outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content);

    [Fact]
    public void StoredCustomerLoadsBackWithItsIdentityAndItsOrders()
    {
        fixture.SkipIfUnavailable();

        var outputs = DapperSourceEntities.Convert(ORMEnum.NHibernate, fixture.CatalogReader).Sources;

        NHibernateAcceptance.UseSessionFactory(
            CompileEntities(outputs),
            Mappings(outputs),
            (factory, entities) =>
            {
                var customerType = entities.GetType("DapperEntities.Customer")!;
                var orderType = entities.GetType("DapperEntities.Order")!;

                using var connection = fixture.OpenConnection();
                using var session = factory.WithOptions().Connection(connection).OpenSession();
                using var transaction = session.BeginTransaction();

                var placedAt = new DateTime(2026, 8, 24, 10, 30, 0);

                dynamic customer = Activator.CreateInstance(customerType)!;
                customer.Name = "Persisted customer";
                var customerId = (int)session.Save((object)customer);

                int[] orderIds = [1, 2];
                foreach (var orderId in orderIds)
                {
                    dynamic order = Activator.CreateInstance(orderType)!;
                    order.CompanyId = 7;
                    order.OrderId = orderId;
                    order.OrderDate = new DateTime(2026, 8, 24);
                    order.PlacedAt = placedAt;
                    order.IsCancelled = false;
                    // The many-to-one owns the write on the CustomerId column; the scalar
                    // property is mapped insert="false" update="false" beside it.
                    order.Customer = customer;
                    session.Save((object)order);
                }

                session.Flush();
                session.Clear();

                var reloaded = session.Get(customerType, customerId);
                Assert.NotNull(reloaded);
                dynamic loadedCustomer = reloaded!;
                Assert.Equal(customerId, (int)loadedCustomer.CustomerId);
                Assert.Equal("Persisted customer", (string)loadedCustomer.Name);

                // Counting initializes the persistent bag NHibernate assigned to the
                // interface-declared property when it hydrated the entity - the class of
                // defect only this level sees (decision 035).
                Assert.Equal(2, (int)loadedCustomer.Orders.Count);

                // Flat composite-id: the entity is its own key class, so this lookup runs
                // the generated identity members (decision 006) inside NHibernate.
                dynamic key = Activator.CreateInstance(orderType)!;
                key.CompanyId = 7;
                key.OrderId = 2;
                var reloadedOrder = session.Get(orderType, (object)key);
                Assert.NotNull(reloadedOrder);
                dynamic loadedOrder = reloadedOrder!;
                Assert.Equal(placedAt, (DateTime)loadedOrder.PlacedAt);
                Assert.Equal(customerId, (int)loadedOrder.Customer.CustomerId);

                transaction.Rollback();
            });
    }

    /// <summary>
    /// The negative half of the level, and its reason to exist: a collection declared by
    /// its concrete type instead of the interface compiles (level 2) and builds a session
    /// factory (level 3 - the mapping never names the property's type), and is refused
    /// only when NHibernate assigns its persistent wrapper to the property at runtime.
    /// This is the defect decision 035 fixed and level 1 guards by descriptor marks;
    /// here the enforcement is undone on the class alone to show the runtime failure the
    /// other levels cannot see.
    /// </summary>
    [Fact]
    public void AConcreteCollectionDeclarationIsRefusedFirstAtThisLevel()
    {
        fixture.SkipIfUnavailable();

        var outputs = DapperSourceEntities.Convert(ORMEnum.NHibernate, fixture.CatalogReader).Sources;

        var tamperedSources = outputs
            .Where(o => o.ContentType == ConversionContentType.CSharpEntity)
            .Select(o => o.Content.Replace("IList<", "List<", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(tamperedSources, s => s.Contains("virtual List<", StringComparison.Ordinal));

        var compiled = GeneratedEntityCompiler.CompileOrFail(
            DapperSourceEntities.AssemblyName, tamperedSources, GeneratedEntityCompiler.NHibernateConsumerReferences);

        NHibernateAcceptance.UseSessionFactory(
            compiled,
            Mappings(outputs),
            (factory, entities) =>
            {
                var customerType = entities.GetType("DapperEntities.Customer")!;

                using var connection = fixture.OpenConnection();
                using var session = factory.WithOptions().Connection(connection).OpenSession();
                // Never committed - disposing rolls back whatever the failing scenario
                // managed to write before it was stopped.
                using var transaction = session.BeginTransaction();

                var thrown = Assert.Throws<PropertyAccessException>(() =>
                {
                    dynamic customer = Activator.CreateInstance(customerType)!;
                    customer.Name = "Concrete collection";
                    var customerId = (int)session.Save((object)customer);
                    session.Flush();
                    session.Clear();
                    session.Get(customerType, customerId);
                });

                // "Invalid Cast (check your mapping for property type mismatches);
                // setter of DapperEntities.Customer" - the refusal names the class,
                // not the property.
                Assert.Contains("DapperEntities.Customer", thrown.Message);
            });
    }
}
