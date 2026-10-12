using AbstractWrappers.Diagnostics;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The generating half of decision 005: a many-to-many stated by the source becomes an
/// explicit junction entity with two owning many-to-one relations, and both collections
/// retarget to it as inverse one-to-many. Everything the entity is made of is a stated
/// fact - the junction table, its columns towards both sides - except the class name,
/// which derives from the table name and is reported as a convention.
/// </summary>
public class JunctionEntitySynthesisTest
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

    private static NHibernateEntityBuilder ParseAll(params string[] xmlOverrides)
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(SupplierSource);
        entityParser.Parse(ProductSource);

        var mappings = xmlOverrides.Length > 0 ? xmlOverrides : new[] { SupplierMapping, ProductMapping };
        foreach (var xml in mappings)
        {
            mappingParser.Parse(xml);
        }

        return builder;
    }

    [Fact]
    public void SynthesizesTheJunctionEntityFromTheStatedFacts()
    {
        var builder = ParseAll();
        builder.Build();

        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");

        Assert.True(junction.IsJunctionTable);
        Assert.Equal("ProductSuppliers", junction.Table);
        Assert.Equal("NHibernateEntities", junction.Entity.Namespace);
        Assert.Equal(["SupplierId", "ProductId"],
            junction.PrimaryKey!.Parts.Select(p => p.PropertyMap.Property.Name));
        Assert.All(junction.PrimaryKey.Parts, p => Assert.Equal(PrimaryKeyStrategy.Assigned, p.Strategy));

        // Two owning many-to-one relations, each paired with the key it references.
        Assert.Equal(2, junction.Relations.Count);
        var toSupplier = Assert.Single(junction.Relations, r => r.TargetEntity == "Supplier");
        Assert.Equal(Cardinality.ManyToOne, toSupplier.Cardinality);
        Assert.Equal(RelationRole.Owning, toSupplier.Role);
        var pair = Assert.Single(toSupplier.ColumnPairs);
        Assert.Equal("SupplierId", pair.Source.Property.Name);
        Assert.Equal("SupplierId", pair.Target.Property.Name);

        // The key part's facts travel to the foreign key column that references it.
        var keyType = junction.PropertyMaps.Single(pm => pm.Property.Name == "SupplierId");
        Assert.Equal(ScalarType.Int, keyType.Property.Type?.ScalarType);
        Assert.Equal(DatabaseType.Integer, keyType.Type);
        Assert.False(keyType.IsNullable);
    }

    [Fact]
    public void BothCollectionsRetargetToTheJunctionEntity()
    {
        var builder = ParseAll();
        builder.Build();

        var supplier = builder.EntityMaps.Single(em => em.Entity.Name == "Supplier");
        var relation = Assert.Single(supplier.Relations);

        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal(RelationRole.Inverse, relation.Role);
        Assert.Equal("ProductSupplier", relation.TargetEntity);

        // The collection now holds the junction entity, and its key pairs with this side.
        var products = supplier.PropertyMaps.Single(pm => pm.Property.Name == "Products");
        Assert.Equal("ProductSupplier", products.Property.Type?.ElementType?.TargetEntity);
        var pair = Assert.Single(relation.ColumnPairs);
        Assert.Equal("SupplierId", pair.Source.Property.Name);

        var product = builder.EntityMaps.Single(em => em.Entity.Name == "Product");
        Assert.Equal("ProductSupplier", Assert.Single(product.Relations).TargetEntity);

        // The class name is the tool's convention and says so.
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Convention && r.Entity == "ProductSupplier");
    }

    [Fact]
    public void JunctionMappingRendersAsAnOrdinaryEntity()
    {
        var builder = ParseAll();
        var outputs = builder.Build();

        var junctionXml = outputs.Where(o => o.ContentType == ConversionContentType.XML)
            .Single(o => o.Content.Contains("<class name=\"ProductSupplier\""));

        Assert.Contains("table=\"ProductSuppliers\"", junctionXml.Content);
        Assert.Contains("<composite-id>", junctionXml.Content);
        Assert.Contains("<key-property name=\"SupplierId\"", junctionXml.Content);
        Assert.Contains("<key-property name=\"ProductId\"", junctionXml.Content);

        // The relation columns are the key columns, so the reference is read-only and the
        // identifier keeps the write - otherwise NHibernate refuses the repeated column.
        Assert.Contains(
            "<many-to-one name=\"Supplier\" class=\"Supplier\" column=\"SupplierId\" insert=\"false\" update=\"false\" />",
            junctionXml.Content);

        var supplierXml = outputs.Where(o => o.ContentType == ConversionContentType.XML)
            .Single(o => o.Content.Contains("table=\"Suppliers\""));

        Assert.Contains("<key column=\"SupplierId\" />", supplierXml.Content);
        Assert.Contains("<one-to-many class=\"ProductSupplier\" />", supplierXml.Content);
        Assert.DoesNotContain("<many-to-many", supplierXml.Content);
    }

    [Fact]
    public void AUnidirectionalManyToManySynthesizesFromOneSideAlone()
    {
        const string productWithoutCollection = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.Product, NHibernateEntities" table="Products">
                    <id name="ProductId" column="ProductId" type="int">
                        <generator class="identity" />
                    </id>
                    <property name="ProductName" not-null="true" length="100" />
                </class>
            </hibernate-mapping>
            """;

        // One collection element carries everything: its <key>, its <many-to-many> columns
        // and the table. The far side needs no collection of its own.
        var builder = ParseAll(SupplierMapping, productWithoutCollection);
        builder.Build();

        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");
        Assert.Equal(2, junction.Relations.Count);

        var supplier = builder.EntityMaps.Single(em => em.Entity.Name == "Supplier");
        Assert.Equal("ProductSupplier", Assert.Single(supplier.Relations).TargetEntity);
    }

    [Fact]
    public void WithoutAStatedJunctionTableTheRelationStaysManyToMany()
    {
        const string supplierWithoutTable = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.Supplier, NHibernateEntities" table="Suppliers">
                    <id name="SupplierId" column="SupplierId" type="int">
                        <generator class="identity" />
                    </id>
                    <property name="SupplierName" not-null="true" length="100" />
                    <bag name="Products">
                        <key column="SupplierId" />
                        <many-to-many class="Product" column="ProductId" />
                    </bag>
                </class>
            </hibernate-mapping>
            """;

        const string productWithoutTable = """
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="NHibernateEntities">
                <class name="NHibernateEntities.Product, NHibernateEntities" table="Products">
                    <id name="ProductId" column="ProductId" type="int">
                        <generator class="identity" />
                    </id>
                    <property name="ProductName" not-null="true" length="100" />
                    <bag name="Suppliers">
                        <key column="ProductId" />
                        <many-to-many class="Supplier" column="SupplierId" />
                    </bag>
                </class>
            </hibernate-mapping>
            """;

        var builder = ParseAll(supplierWithoutTable, productWithoutTable);
        var outputs = builder.Build();

        // No table, no synthesis: an entity cannot stand on a table nobody named. The
        // relation stays many-to-many, the missing junction entity is reported as before.
        Assert.DoesNotContain(builder.EntityMaps, em => em.IsJunctionTable);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("junction"));

        // The facts the source did state - its <key> and <many-to-many> columns - go back
        // out verbatim; only the table nobody named stays missing.
        var supplierXml = outputs.Where(o => o.ContentType == ConversionContentType.XML)
            .Single(o => o.Content.Contains("table=\"Suppliers\""));
        Assert.Contains("<many-to-many class=\"Product\" column=\"ProductId\" />", supplierXml.Content);
        Assert.Contains("<key column=\"SupplierId\" />", supplierXml.Content);
    }

    /// <summary>
    /// The initializer follows the element type the collection now has: a type argument that
    /// named the far entity names the junction entity, so a target that keeps the C#
    /// initializer - EF Core here - writes a class that compiles. A form that names no element
    /// type stays as it was.
    /// </summary>
    [Fact]
    public void ATypedInitializerFollowsTheJunctionEntity()
    {
        var supplier = SupplierSource.Replace("List<Product> Products { get; set; } = [];", "ISet<Product> Products { get; set; } = new HashSet<Product>();", StringComparison.Ordinal);
        var product = ProductSource.Replace("List<Supplier> Suppliers { get; set; } = [];", "IList<Supplier> Suppliers { get; set; } = new List<NHibernateEntities.Supplier>();", StringComparison.Ordinal);

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore,
        [
            new() { ContentType = ConversionContentType.CSharp, Content = supplier },
            new() { ContentType = ConversionContentType.CSharp, Content = product },
            new() { ContentType = ConversionContentType.XML, Content = SupplierMapping },
            new() { ContentType = ConversionContentType.XML, Content = ProductMapping },
        ]);

        var classes = result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).ToList();
        Assert.Contains(classes, c => c.Content.Contains("Products { get; set; } = new HashSet<ProductSupplier>();", StringComparison.Ordinal));
        Assert.Contains(classes, c => c.Content.Contains("Suppliers { get; set; } = new List<ProductSupplier>();", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("initializer", StringComparison.Ordinal));

        GeneratedEntityCompiler.CompileOrFail(
            "JunctionInitializer_EFCore",
            classes.Select(c => c.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences);
    }

    /// <summary>An initializer that holds elements of the far entity cannot hold junction rows: it is dropped, and the record says so.</summary>
    [Fact]
    public void AnInitializerWithElementsOfTheFarEntityIsDroppedWithALoss()
    {
        var builder = new NHibernateEntityBuilder();
        var entityParser = new NHibernateEntityParser(builder);
        var mappingParser = new NHibernateXMLMappingParser(builder);

        entityParser.Parse(SupplierSource.Replace("= [];", "= new List<Product> { new Product() };", StringComparison.Ordinal));
        entityParser.Parse(ProductSource);
        mappingParser.Parse(SupplierMapping);
        mappingParser.Parse(ProductMapping);
        builder.Build();

        var products = builder.EntityMaps.Single(em => em.Entity.Name == "Supplier")
            .PropertyMaps.Single(pm => pm.Property.Name == "Products");
        Assert.Null(products.Property.DefaultValue);

        var loss = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "Products");
        Assert.Contains("ProductSupplier", loss.Reason, StringComparison.Ordinal);
    }
}
