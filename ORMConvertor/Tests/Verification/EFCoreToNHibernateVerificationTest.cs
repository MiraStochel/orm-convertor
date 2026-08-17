using AbstractWrappers;
using EFCoreWrappers;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the EF Core → NHibernate
/// conversion: the generated classes compile, the generated mappings are valid against the
/// schema shipped with NHibernate, and NHibernate builds a session factory from both. The
/// source expresses the key, so the whole run is dry - no database takes part. The shape of
/// the artifacts stays the business of the shape tests; here nothing is asserted about the
/// text, only about its acceptance.
/// </summary>
public class EFCoreToNHibernateVerificationTest
{
    private const string OrderSource = """
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        [Table("Orders", Schema = "Sales")]
        public class Order
        {
            [Key]
            public required int OrderID { get; set; }

            public required int CustomerID { get; set; }

            [Precision(0)]
            public required DateTime OrderDate { get; set; }

            public string? Comments { get; set; }

            public List<OrderLine> OrderLines { get; set; } = new();
        }
        """;

    private const string OrderLineSource = """
        namespace EFCoreEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        [Table("OrderLines", Schema = "Sales")]
        [PrimaryKey(nameof(OrderID), nameof(OrderLineID))]
        public class OrderLine
        {
            public required int OrderID { get; set; }

            public required int OrderLineID { get; set; }

            [MaxLength(100)]
            public required string Description { get; set; }

            public required int Quantity { get; set; }
        }
        """;

    private static List<ConversionSource> Convert()
    {
        AbstractEntityBuilder builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);
        parser.Parse(OrderSource);
        parser.Parse(OrderLineSource);
        return builder.Build();
    }

    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs)
        => GeneratedEntityCompiler.CompileOrFail(
            "EFCoreEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

    [Fact]
    public void GeneratedEntitiesCompile()
    {
        CompileEntities(Convert());
    }

    [Fact]
    public void GeneratedMappingsAreValidAgainstTheSchema()
    {
        var mappings = Convert().Where(o => o.ContentType == ConversionContentType.XML).ToList();
        Assert.Equal(2, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts()
    {
        var outputs = Convert();

        // Completing without an exception is the verdict: NHibernate bound both mappings to
        // the compiled classes - key order, identity members, navigation targets included.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
