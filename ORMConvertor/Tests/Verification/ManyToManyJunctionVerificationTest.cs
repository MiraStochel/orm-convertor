using AbstractWrappers;
using EFCoreWrappers;
using Microsoft.EntityFrameworkCore;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the many-to-many synthesis of
/// decision 005: the junction entity nobody wrote compiles together with the retargeted
/// sides, and both target frameworks accept the whole. The source states everything the
/// junction is made of, so the run is dry - no database takes part.
/// </summary>
public class ManyToManyJunctionVerificationTest
{
    private const string SupplierSource = """
        namespace NHibernateEntities;

        public class Supplier
        {
            public virtual int SupplierId { get; set; }

            public virtual string SupplierName { get; set; } = string.Empty;

            public virtual List<Product> Products { get; set; } = [];
        }
        """;

    private const string ProductSource = """
        namespace NHibernateEntities;

        public class Product
        {
            public virtual int ProductId { get; set; }

            public virtual string ProductName { get; set; } = string.Empty;

            public virtual List<Supplier> Suppliers { get; set; } = [];
        }
        """;

    private const string SupplierMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Supplier, NHibernateEntities" table="Suppliers">
                <id name="SupplierId" column="SupplierId" type="int">
                    <generator class="identity" />
                </id>
                <property name="SupplierName" not-null="true" length="100" />
                <bag name="Products" table="ProductSuppliers">
                    <key column="SupplierId" />
                    <many-to-many class="Product" column="ProductId" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    private const string ProductMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
            <class name="NHibernateEntities.Product, NHibernateEntities" table="Products">
                <id name="ProductId" column="ProductId" type="int">
                    <generator class="identity" />
                </id>
                <property name="ProductName" not-null="true" length="100" />
                <bag name="Suppliers" table="ProductSuppliers">
                    <key column="ProductId" />
                    <many-to-many class="Supplier" column="SupplierId" />
                </bag>
            </class>
        </hibernate-mapping>
        """;

    private static List<ConversionSource> Convert(AbstractEntityBuilder builder)
    {
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);
        entityParser.Parse(SupplierSource);
        entityParser.Parse(ProductSource);
        mappingParser.Parse(SupplierMapping);
        mappingParser.Parse(ProductMapping);
        return builder.Build();
    }

    private static byte[] CompileEntities(
        IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            "NHibernateEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void NHibernateBuildsASessionFactoryIncludingTheJunctionEntity()
    {
        var outputs = Convert(new NHibernateEntityBuilder());

        var mappings = outputs.Where(o => o.ContentType == ConversionContentType.XML).ToList();
        Assert.Equal(3, mappings.Count);

        Assert.All(mappings, mapping =>
        {
            var errors = NHibernateMappingSchema.Validate(mapping.Content);
            Assert.True(errors.Count == 0, "Generated mapping is invalid:"
                + Environment.NewLine + string.Join(Environment.NewLine, errors));
        });

        // Completing without an exception is the verdict: NHibernate bound the synthesized
        // junction - composite key, read-only references over its columns - and both
        // retargeted collections to the compiled classes.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            mappings.Select(m => m.Content));
    }

    [Fact]
    public void EFCoreBuildsAValidatedModelIncludingTheJunctionEntity()
    {
        var outputs = Convert(new EFCoreEntityBuilder());

        var model = EFCoreAcceptance.BuildModel(
            CompileEntities(outputs, GeneratedEntityCompiler.EFCoreConsumerReferences));

        var junction = model.FindEntityType("NHibernateEntities.ProductSupplier");
        Assert.NotNull(junction);
        Assert.Equal("ProductSuppliers", junction.GetTableName());
        Assert.Equal(["SupplierId", "ProductId"],
            junction.FindPrimaryKey()!.Properties.Select(p => p.Name));

        // Two relationships, each pairing the junction's navigation with a retargeted
        // collection of the respective side.
        var foreignKeys = junction.GetForeignKeys().ToList();
        Assert.Equal(2, foreignKeys.Count);

        var toSupplier = Assert.Single(foreignKeys,
            fk => fk.PrincipalEntityType.Name == "NHibernateEntities.Supplier");
        Assert.Equal(["SupplierId"], toSupplier.Properties.Select(p => p.Name));
        Assert.Equal("Products", toSupplier.PrincipalToDependent?.Name);

        var toProduct = Assert.Single(foreignKeys,
            fk => fk.PrincipalEntityType.Name == "NHibernateEntities.Product");
        Assert.Equal(["ProductId"], toProduct.Properties.Select(p => p.Name));
        Assert.Equal("Suppliers", toProduct.PrincipalToDependent?.Name);
    }
}
