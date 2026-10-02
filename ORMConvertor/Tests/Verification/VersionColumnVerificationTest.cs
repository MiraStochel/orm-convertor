using AbstractWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Catalog;
using Tests.Combined;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the version column
/// (decision 030). An EF Core entity with [Timestamp], completed from a catalog stating a
/// rowversion column, becomes an NHibernate mapping whose version element the schema
/// validates and the session factory accepts - including the binding of the generated
/// class, which is where an inexpressible version type would surface - and stays a
/// rowversion in EF Core. A numeric version a source framework increments itself becomes,
/// in EF Core's own model, a concurrency token the application writes: under [Timestamp]
/// EF Core read it as generated on add or update, and nothing generates an int column.
/// </summary>
public class VersionColumnVerificationTest
{
    private const string DocumentSource = """
        namespace VersionedEntities;

        using System.ComponentModel.DataAnnotations;

        public class Document
        {
            [Key]
            public int DocumentID { get; set; }

            [Timestamp]
            public byte[] RowVersion { get; set; }
        }
        """;

    private static List<ConversionSource> Convert(AbstractEntityBuilder builder)
    {
        new EFCoreEntityParser(builder).Parse(DocumentSource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(new TableImage
        {
            Schema = "dbo",
            Name = "Documents",
            Columns =
            [
                new ColumnImage { Name = "DocumentID", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage
                {
                    Name = "RowVersion",
                    Type = DatabaseType.VarBinary,
                    SourceSqlType = "rowversion",
                    Length = 8,
                    IsNullable = false,
                    IsIdentity = false,
                    IsRowVersion = true,
                },
            ],
            PrimaryKeyColumns = ["DocumentID"],
            ForeignKeys = [],
        }));

        return builder.Build();
    }

    private static IModel BuildEFCoreModel(string assemblyName, IEnumerable<ConversionSource> outputs)
        => EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            assemblyName,
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences));

    [Fact]
    public void GeneratedMappingIsValidAgainstTheSchema()
    {
        var mapping = Convert(new NHibernateEntityBuilder()).Single(o => o.ContentType == ConversionContentType.XML);

        var errors = NHibernateMappingSchema.Validate(mapping.Content);
        Assert.True(errors.Count == 0, "Generated mapping is invalid:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts()
    {
        var outputs = Convert(new NHibernateEntityBuilder());

        NHibernateAcceptance.BuildSessionFactory(
            GeneratedEntityCompiler.CompileOrFail(
                "VersionedEntities",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }

    [Fact]
    public void EFCoreKeepsABinaryVersionAsARowVersion()
    {
        var model = BuildEFCoreModel("VersionedEntities_EFCore", Convert(new EFCoreEntityBuilder()));

        var rowVersion = model.FindEntityType("VersionedEntities.Document")?.FindProperty("RowVersion");
        Assert.NotNull(rowVersion);
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    [Theory]
    [MemberData(nameof(VersionColumnTest.FrameworkIncrementedSources), MemberType = typeof(VersionColumnTest))]
    public void EFCoreWritesANumericVersionAndComparesIt(ORMEnum source)
    {
        var result = VersionColumnTest.ConvertNumericVersion(source, ORMEnum.EFCore);
        var model = BuildEFCoreModel($"NumericVersion_From{source}", result.Sources);

        var revision = Assert.Single(model.GetEntityTypes()).FindProperty("Revision");
        Assert.NotNull(revision);
        Assert.True(revision.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, revision.ValueGenerated);
        Assert.Equal("int", revision.GetColumnType());
    }
}
