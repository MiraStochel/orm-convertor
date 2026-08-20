using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Microsoft.EntityFrameworkCore;
using Model;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// The F6 scenario against EF Core (decisions 015 and 016): the catalog completes a bare
/// Dapper source and the finalized EF Core model is asked back for the facts - table and
/// schema names, key parts in order, foreign keys. The schema script is the expected
/// answer, so what comes out of the model is exactly what F6's criterion asks for.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperToEFCoreVerificationTest(TestSchemaFixture fixture)
{
    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences);

    [Fact]
    public void CompletedConversionRefusesNothing()
    {
        fixture.SkipIfUnavailable();

        var result = DapperSourceEntities.Convert(ORMEnum.EFCore);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(CatalogConnectionState.Reached, result.CatalogState);
        Assert.NotNull(result.CatalogReadTime);
    }

    [Fact]
    public void GeneratedEntitiesCompile()
    {
        fixture.SkipIfUnavailable();

        CompileEntities(DapperSourceEntities.Convert(ORMEnum.EFCore).Sources);
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelWithTheCatalogFacts()
    {
        fixture.SkipIfUnavailable();

        var model = EFCoreAcceptance.BuildModel(
            CompileEntities(DapperSourceEntities.Convert(ORMEnum.EFCore).Sources));

        // The framework's own reading of the artifact: everything below came from the
        // catalog, because a Dapper source cannot state any of it (F6).
        var order = model.FindEntityType("DapperEntities.Order");
        Assert.NotNull(order);
        Assert.Equal("Orders", order.GetTableName());
        Assert.Equal(TestDatabase.SchemaName, order.GetSchema());
        Assert.Equal(["CompanyId", "OrderId"],
            order.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var orderToCustomer = Assert.Single(order.GetForeignKeys());
        Assert.Equal("DapperEntities.Customer", orderToCustomer.PrincipalEntityType.Name);
        Assert.Equal(["CustomerId"], orderToCustomer.Properties.Select(p => p.Name));

        // The inverse collection pairs with the same relationship rather than spawning
        // a second one - the catalog supplied both sides of the same foreign key.
        Assert.Equal("Orders", orderToCustomer.PrincipalToDependent?.Name);

        var orderLine = model.FindEntityType("DapperEntities.OrderLine");
        Assert.NotNull(orderLine);
        Assert.Equal(["CompanyId", "OrderId", "LineNo"],
            orderLine.FindPrimaryKey()!.Properties.Select(p => p.Name));

        var lineToOrder = Assert.Single(orderLine.GetForeignKeys());
        Assert.Equal("DapperEntities.Order", lineToOrder.PrincipalEntityType.Name);
        Assert.Equal(["CompanyId", "OrderId"], lineToOrder.Properties.Select(p => p.Name));
        Assert.Equal("OrderLines", lineToOrder.PrincipalToDependent?.Name);
    }
}
