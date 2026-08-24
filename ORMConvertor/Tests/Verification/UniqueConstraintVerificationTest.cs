using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the unique constraint
/// (decision 055). The claim of the descriptors is that both annotation targets can express
/// the fact; what settles it is not the emitted text but the frameworks themselves - EF
/// Core building a model that holds a unique index, and NHibernate validating the mapping
/// against its schema and binding it into a session factory.
/// </summary>
public class UniqueConstraintVerificationTest
{
    private const string ProductSource = """
        namespace ConstrainedEntities;

        using System.ComponentModel.DataAnnotations;

        public class Product
        {
            [Key]
            public int ProductId { get; set; }

            public string Sku { get; set; }

            public string RegionCode { get; set; }

            public string Name { get; set; }
        }
        """;

    private static List<ConversionSource> Convert<TBuilder>(TBuilder builder)
        where TBuilder : AbstractWrappers.AbstractEntityBuilder
    {
        new EFCoreEntityParser(builder).Parse(ProductSource);
        builder.AddTable("Products");

        // One constraint over a single column and one over two, so both spellings of the
        // fact reach the artifact in one document.
        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);
        builder.AddUniqueConstraint("UQ_Products_RegionName", ["RegionCode", "Name"]);

        return builder.Build();
    }

    [Fact]
    public void EFCoreBuildsAModelCarryingBothConstraintsAsUniqueIndexes()
    {
        var outputs = Convert(new EFCoreEntityBuilder());

        var model = EFCoreAcceptance.BuildModel(
            GeneratedEntityCompiler.CompileOrFail(
                "ConstrainedEntitiesEFCore",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.EFCoreConsumerReferences));

        var product = Assert.Single(model.GetEntityTypes());
        var indexes = product.GetIndexes().ToList();

        Assert.Equal(2, indexes.Count);
        Assert.All(indexes, index => Assert.True(index.IsUnique));

        Assert.Contains(indexes, index =>
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "Sku" }));
        Assert.Contains(indexes, index =>
            index.Properties.Select(p => p.Name).SequenceEqual(new[] { "RegionCode", "Name" }));
    }

    [Fact]
    public void GeneratedNHibernateMappingIsValidAgainstTheSchema()
    {
        var mapping = Convert(new NHibernateEntityBuilder())
            .Single(o => o.ContentType == ConversionContentType.XML);

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
                "ConstrainedEntitiesNHibernate",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
