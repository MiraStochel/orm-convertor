using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Model;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// Fourth verification level of decision 016 for the many-to-many: the synthesized
/// junction entity (decision 005) is actually used - the association is stored as a row
/// of it and walked back through it to the far side. The scenario continues where
/// <see cref="DapperManyToManyVerificationTest"/> ends, over the same artifacts, and
/// follows the fixture's rule for writing tests: its own transaction over
/// <c>OpenConnection()</c>, rolled back at the end. The entity types exist only at
/// runtime, so <c>dynamic</c> stands in for the compile-time reference a consumer
/// project would have.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperManyToManyPersistenceTest(TestSchemaFixture fixture)
{
    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperJunctionSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateStoresTheAssociationAndWalksItBackThroughTheJunction()
    {
        fixture.SkipIfUnavailable();

        var outputs = DapperJunctionSourceEntities.Convert(ORMEnum.NHibernate, fixture.CatalogReader).Sources;

        NHibernateAcceptance.UseSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content),
            (factory, entities) =>
            {
                var productType = entities.GetType("DapperJunctionEntities.Product")!;
                var supplierType = entities.GetType("DapperJunctionEntities.Supplier")!;
                var junctionType = entities.GetType("DapperJunctionEntities.ProductSupplier")!;

                using var connection = fixture.OpenConnection();
                using var session = factory.WithOptions().Connection(connection).OpenSession();
                using var transaction = session.BeginTransaction();

                dynamic product = Activator.CreateInstance(productType)!;
                // Products has no IDENTITY, so the application assigns the key.
                product.ProductId = 42;
                product.ProductName = "Junction product";
                product.Sku = "SKU-42";
                product.UnitPrice = 12.3456m;
                product.IsDiscontinued = false;
                product.LastModified = new DateTime(2026, 8, 25, 9, 0, 0);
                session.Save((object)product);

                dynamic supplier = Activator.CreateInstance(supplierType)!;
                supplier.SupplierName = "Junction supplier";
                var supplierId = (int)session.Save((object)supplier);

                // The association is a row of the junction entity: both collections are
                // mapped inverse, so this is the only write that records it, and the key
                // halves are set by hand because the composite id owns their columns.
                dynamic link = Activator.CreateInstance(junctionType)!;
                link.ProductId = 42;
                link.SupplierId = supplierId;
                session.Save((object)link);

                session.Flush();
                session.Clear();

                var reloaded = session.Get(productType, 42);
                Assert.NotNull(reloaded);
                dynamic loadedProduct = reloaded!;

                // The collection the source declared with Supplier elements now holds the
                // junction entity (decision 005); walking it crosses the junction table
                // to the far side.
                Assert.Equal(1, (int)loadedProduct.Suppliers.Count);
                dynamic loadedLink = loadedProduct.Suppliers[0];
                Assert.Equal(supplierId, (int)loadedLink.Supplier.SupplierId);
                Assert.Equal("Junction supplier", (string)loadedLink.Supplier.SupplierName);

                // Flat composite-id over the synthesized key: the junction entity is its
                // own key class, so this lookup runs the generated identity members
                // (decision 006) inside NHibernate.
                dynamic key = Activator.CreateInstance(junctionType)!;
                key.ProductId = 42;
                key.SupplierId = supplierId;
                Assert.NotNull(session.Get(junctionType, (object)key));

                transaction.Rollback();
            });
    }

    [Fact]
    public void EFCoreStoresTheAssociationAndWalksItBackThroughTheJunction()
    {
        fixture.SkipIfUnavailable();

        var entities = Assembly.Load(CompileEntities(
            DapperJunctionSourceEntities.Convert(ORMEnum.EFCore, fixture.CatalogReader).Sources,
            GeneratedEntityCompiler.EFCoreConsumerReferences));
        var productType = entities.GetType("DapperJunctionEntities.Product")!;
        var supplierType = entities.GetType("DapperJunctionEntities.Supplier")!;
        var junctionType = entities.GetType("DapperJunctionEntities.ProductSupplier")!;

        using var connection = fixture.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var context = EFCoreAcceptance.OpenContext(entities, connection);
        context.Database.UseTransaction(transaction);

        dynamic product = Activator.CreateInstance(productType)!;
        // Products has no IDENTITY, so the application assigns the key.
        product.ProductId = 42;
        product.ProductName = "Junction product";
        product.Sku = "SKU-42";
        product.UnitPrice = 12.3456m;
        product.IsDiscontinued = false;
        product.LastModified = new DateTime(2026, 8, 25, 9, 0, 0);

        dynamic supplier = Activator.CreateInstance(supplierType)!;
        supplier.SupplierName = "Junction supplier";

        // Both principals are reached over the junction's navigations, so SaveChanges has
        // to write both halves of the composite key from the relationships the catalog
        // supplied - the supplier's half only exists after its identity insert.
        dynamic link = Activator.CreateInstance(junctionType)!;
        link.Product = product;
        link.Supplier = supplier;

        context.Add((object)link);
        context.SaveChanges();

        var supplierId = (int)supplier.SupplierId;
        Assert.NotEqual(0, supplierId);
        Assert.Equal(42, (int)link.ProductId);
        Assert.Equal(supplierId, (int)link.SupplierId);

        context.ChangeTracker.Clear();

        var reloaded = context.Find(productType, 42);
        Assert.NotNull(reloaded);
        dynamic loadedProduct = reloaded!;

        // No lazy loading here - the collection of junction entities is loaded
        // explicitly, the way a consumer of the generated entities would.
        context.Entry(reloaded!).Collection("Suppliers").Load();
        Assert.Equal(1, (int)loadedProduct.Suppliers.Count);

        dynamic loadedLink = loadedProduct.Suppliers[0];
        context.Entry((object)loadedLink).Reference("Supplier").Load();
        Assert.Equal(supplierId, (int)loadedLink.Supplier.SupplierId);
        Assert.Equal("Junction supplier", (string)loadedLink.Supplier.SupplierName);

        transaction.Rollback();
    }
}
