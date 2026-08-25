using System.Reflection;
using DatabaseCatalog;
using Microsoft.EntityFrameworkCore;
using Model;
using OrmConvertor;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// Fourth verification level of decision 016 for the one-to-one: a profile sharing its
/// key with its customer is stored and loaded back through the generated artifacts. The
/// relation exists only in the catalog - the source states nothing but the navigation,
/// and the foreign key of CustomerProfiles covers its whole primary key, which is what
/// the completion reads as the shared-key one-to-one (decisions 012 and 015). The
/// scenario follows the fixture's rule for writing tests: its own transaction over
/// <c>OpenConnection()</c>, rolled back at the end. The entity types exist only at
/// runtime, so <c>dynamic</c> stands in for the compile-time reference a consumer
/// project would have.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperOneToOnePersistenceTest(TestSchemaFixture fixture)
{
    private const string AssemblyName = "DapperProfileEntities";

    private const string CustomerSource = """
        namespace DapperProfileEntities;

        public class Customer
        {
            public int CustomerId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Notes { get; set; }
        }
        """;

    private const string ProfileSource = """
        namespace DapperProfileEntities;

        public class CustomerProfile
        {
            public int CustomerId { get; set; }

            public string? Website { get; set; }

            public decimal? CreditLimit { get; set; }

            public Customer Customer { get; set; } = null!;
        }
        """;

    private ConversionResult Convert(ORMEnum target)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = CustomerSource },
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = ProfileSource },
            ],
            fixture.CatalogReader);

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateStoresTheProfileAndLoadsItBackOverTheSharedKey()
    {
        fixture.SkipIfUnavailable();

        var outputs = Convert(ORMEnum.NHibernate).Sources;

        NHibernateAcceptance.UseSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content),
            (factory, entities) =>
            {
                var customerType = entities.GetType("DapperProfileEntities.Customer")!;
                var profileType = entities.GetType("DapperProfileEntities.CustomerProfile")!;

                using var connection = fixture.OpenConnection();
                using var session = factory.WithOptions().Connection(connection).OpenSession();
                using var transaction = session.BeginTransaction();

                dynamic customer = Activator.CreateInstance(customerType)!;
                customer.Name = "Profiled customer";
                var customerId = (int)session.Save((object)customer);

                // The shared key is assigned by hand: the identifier owns the write on
                // CustomerId, the reference beside it is mapped read-only.
                dynamic profile = Activator.CreateInstance(profileType)!;
                profile.CustomerId = customerId;
                profile.Website = "https://example.test";
                profile.CreditLimit = 1234.50m;
                session.Save((object)profile);

                session.Flush();
                session.Clear();

                var reloaded = session.Get(profileType, customerId);
                Assert.NotNull(reloaded);
                dynamic loadedProfile = reloaded!;
                Assert.Equal("https://example.test", (string)loadedProfile.Website);
                Assert.Equal(1234.50m, (decimal)loadedProfile.CreditLimit);

                // Walking the reference is the framework executing the one-to-one the
                // catalog supplied: the same key, the other table.
                Assert.Equal(customerId, (int)loadedProfile.Customer.CustomerId);
                Assert.Equal("Profiled customer", (string)loadedProfile.Customer.Name);

                transaction.Rollback();
            });
    }

    [Fact]
    public void EFCoreStoresTheProfileAndLoadsItBackOverTheSharedKey()
    {
        fixture.SkipIfUnavailable();

        var entities = Assembly.Load(CompileEntities(
            Convert(ORMEnum.EFCore).Sources, GeneratedEntityCompiler.EFCoreConsumerReferences));
        var customerType = entities.GetType("DapperProfileEntities.Customer")!;
        var profileType = entities.GetType("DapperProfileEntities.CustomerProfile")!;

        using var connection = fixture.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var context = EFCoreAcceptance.OpenContext(entities, connection);
        context.Database.UseTransaction(transaction);

        dynamic customer = Activator.CreateInstance(customerType)!;
        customer.Name = "Profiled customer";

        // The profile's key is never set by hand: reached over the navigation, SaveChanges
        // has to propagate the customer's generated identity into the shared key the
        // catalog supplied.
        dynamic profile = Activator.CreateInstance(profileType)!;
        profile.Website = "https://example.test";
        profile.CreditLimit = 1234.50m;
        profile.Customer = customer;

        context.Add((object)profile);
        context.SaveChanges();

        var customerId = (int)customer.CustomerId;
        Assert.NotEqual(0, customerId);
        Assert.Equal(customerId, (int)profile.CustomerId);

        context.ChangeTracker.Clear();

        var reloaded = context.Find(profileType, customerId);
        Assert.NotNull(reloaded);
        dynamic loadedProfile = reloaded!;
        Assert.Equal("https://example.test", (string)loadedProfile.Website);
        Assert.Equal(1234.50m, (decimal)loadedProfile.CreditLimit);

        // No lazy loading here - the reference is loaded explicitly, the way a consumer
        // of the generated entities would.
        context.Entry(reloaded!).Reference("Customer").Load();
        Assert.Equal(customerId, (int)loadedProfile.Customer.CustomerId);
        Assert.Equal("Profiled customer", (string)loadedProfile.Customer.Name);

        transaction.Rollback();
    }
}
