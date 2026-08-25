using AbstractWrappers.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Model;
using OrmConvertor;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// The many-to-many nobody's artifact expresses, judged by the frameworks (decisions 005,
/// 015 and 016): from the Dapper source of <see cref="DapperJunctionSourceEntities"/> the
/// conversion has to come out with a synthesized junction entity both targets accept. The
/// junction exists only in the catalog, so the scenario needs the database and skips with
/// the rest when none is configured.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperManyToManyVerificationTest(TestSchemaFixture fixture)
{
    private ConversionResult Convert(ORMEnum target)
        => DapperJunctionSourceEntities.Convert(target, fixture.CatalogReader);

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            DapperJunctionSourceEntities.AssemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateBuildsASessionFactoryIncludingTheDetectedJunction()
    {
        fixture.SkipIfUnavailable();

        var result = Convert(ORMEnum.NHibernate);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);

        var mappings = result.Sources.Where(o => o.ContentType == ConversionContentType.XML).ToList();
        Assert.Equal(3, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });

        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(result.Sources, GeneratedEntityCompiler.NHibernateConsumerReferences),
            mappings.Select(m => m.Content));
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelIncludingTheDetectedJunction()
    {
        fixture.SkipIfUnavailable();

        var result = Convert(ORMEnum.EFCore);

        var model = EFCoreAcceptance.BuildModel(
            CompileEntities(result.Sources, GeneratedEntityCompiler.EFCoreConsumerReferences));

        // Everything about the junction came out of the catalog: the table, the key, the
        // two relationships paired with the source-declared collections.
        var junction = model.FindEntityType("DapperJunctionEntities.ProductSupplier");
        Assert.NotNull(junction);
        Assert.Equal("ProductSuppliers", junction.GetTableName());
        Assert.Equal(TestDatabase.SchemaName, junction.GetSchema());

        var keyParts = junction.FindPrimaryKey()!.Properties.Select(p => p.Name).ToList();
        Assert.Equal(2, keyParts.Count);
        Assert.Contains("ProductId", keyParts);
        Assert.Contains("SupplierId", keyParts);

        var foreignKeys = junction.GetForeignKeys().ToList();
        Assert.Equal(2, foreignKeys.Count);
        Assert.Equal("Products",
            Assert.Single(foreignKeys, fk => fk.PrincipalEntityType.Name == "DapperJunctionEntities.Supplier")
                .PrincipalToDependent?.Name);
        Assert.Equal("Suppliers",
            Assert.Single(foreignKeys, fk => fk.PrincipalEntityType.Name == "DapperJunctionEntities.Product")
                .PrincipalToDependent?.Name);
    }
}
