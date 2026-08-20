using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Catalog;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the version column
/// (decision 030): an EF Core entity with [Timestamp], completed from a catalog stating a
/// rowversion column, becomes an NHibernate mapping whose version element the schema
/// validates and the session factory accepts - including the binding of the generated
/// class, which is where an inexpressible version type would surface.
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

    private static List<ConversionSource> Convert()
    {
        var builder = new NHibernateEntityBuilder();
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

    [Fact]
    public void GeneratedMappingIsValidAgainstTheSchema()
    {
        var mapping = Convert().Single(o => o.ContentType == ConversionContentType.XML);

        var errors = NHibernateMappingSchema.Validate(mapping.Content);
        Assert.True(errors.Count == 0, "Generated mapping is invalid:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts()
    {
        var outputs = Convert();

        NHibernateAcceptance.BuildSessionFactory(
            GeneratedEntityCompiler.CompileOrFail(
                "VersionedEntities",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
