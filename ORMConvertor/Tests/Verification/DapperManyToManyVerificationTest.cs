using AbstractWrappers.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Model;
using OrmConvertor;
using Tests.Database;

namespace Tests.Verification;

/// <summary>
/// The many-to-many nobody's artifact expresses, judged by the frameworks (decisions 005,
/// 015 and 016): a Dapper source declares only the collections, the schema owns the
/// junction table, and the conversion has to come out with a synthesized junction entity
/// both targets accept. The junction exists only in the catalog, so the scenario needs the
/// database and skips with the rest when none is configured.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DapperManyToManyVerificationTest(TestSchemaFixture fixture)
{
    private const string SupplierSource = """
        namespace DapperJunctionEntities;

        public class Supplier
        {
            public int SupplierId { get; set; }

            public string SupplierName { get; set; } = string.Empty;

            public List<Product> Products { get; set; } = [];
        }
        """;

    private const string ProductSource = """
        namespace DapperJunctionEntities;

        public class Product
        {
            public int ProductId { get; set; }

            public string ProductName { get; set; } = string.Empty;

            public string Sku { get; set; } = string.Empty;

            public decimal UnitPrice { get; set; }

            public bool IsDiscontinued { get; set; }

            public DateTime LastModified { get; set; }

            public List<Supplier> Suppliers { get; set; } = [];
        }
        """;

    private ConversionResult Convert(ORMEnum target)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = SupplierSource },
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = ProductSource },
            ],
            fixture.CatalogReader);

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            "DapperJunctionEntities",
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
