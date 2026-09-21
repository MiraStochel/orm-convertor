using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using EFCoreWrappers;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using Tests.Catalog;

namespace Tests.EFCore;

/// <summary>
/// Collection navigations on both sides pointing at each other are EF Core's many-to-many
/// over an implicit junction table (EF Core 5 and later), not two one-to-many relations.
/// The claim waits for the entities of the conversion like every other convention
/// (decision 067) and the junction table itself stays for the catalog to name, because it
/// is a fact of the schema and decision 005 builds the junction entity from it.
/// </summary>
public class EFCoreManyToManyConventionTest
{
    private const string ProductSource = """
        public class Product
        {
            [Key]
            public int ProductId { get; set; }

            public List<Supplier> Suppliers { get; set; } = [];
        }
        """;

    private const string SupplierSource = """
        public class Supplier
        {
            [Key]
            public int SupplierId { get; set; }

            public List<Product> Products { get; set; } = [];
        }
        """;

    private static TableImage ProductsImage() => new()
    {
        Schema = "sales",
        Name = "Products",
        Columns = [new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true }],
        PrimaryKeyColumns = ["ProductId"],
        ForeignKeys = [],
    };

    private static TableImage SuppliersImage() => new()
    {
        Schema = "sales",
        Name = "Suppliers",
        Columns = [new ColumnImage { Name = "SupplierId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true }],
        PrimaryKeyColumns = ["SupplierId"],
        ForeignKeys = [],
    };

    private static TableImage JunctionImage() => new()
    {
        Schema = "sales",
        Name = "ProductSuppliers",
        Columns =
        [
            new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "SupplierId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["ProductId", "SupplierId"],
        ForeignKeys =
        [
            new ForeignKeyImage
            {
                Name = "FK_ProductSuppliers_Products",
                ReferencedSchema = "sales",
                ReferencedTable = "Products",
                Columns = [new ForeignKeyColumn("ProductId", "ProductId")],
            },
            new ForeignKeyImage
            {
                Name = "FK_ProductSuppliers_Suppliers",
                ReferencedSchema = "sales",
                ReferencedTable = "Suppliers",
                Columns = [new ForeignKeyColumn("SupplierId", "SupplierId")],
            },
        ],
    };

    private static Relation RelationOf(AbstractWrappers.AbstractEntityBuilder builder, string entity)
        => Assert.Single(builder.EntityMaps.Single(em => em.Entity.Name == entity).Relations);

    [Fact]
    public void TwoCollectionsPointingAtEachOtherAreOneManyToMany()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(ProductSource);
        parser.Parse(SupplierSource);

        builder.Build();

        // Each collection used to become an inverse one-to-many at once, so the pair
        // claimed a foreign key on both sides - a shape no schema has.
        Assert.Equal(Cardinality.ManyToMany, RelationOf(builder, "Product").Cardinality);
        Assert.Equal(Cardinality.ManyToMany, RelationOf(builder, "Supplier").Cardinality);
        Assert.Equal("Supplier", RelationOf(builder, "Product").TargetEntity);
        Assert.Equal("Product", RelationOf(builder, "Supplier").TargetEntity);
    }

    [Fact]
    public void WithoutTheJunctionTableTheMissingEntityIsRecorded()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(ProductSource);
        parser.Parse(SupplierSource);

        builder.Build();

        // The source names no junction table - EF Core's is implicit - and no catalog was
        // there to name it, so decision 005 has nothing to synthesize the entity from. The
        // state is the resolution phase's record, not silence.
        Assert.DoesNotContain(builder.EntityMaps, em => em.IsJunctionTable);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Entity == "Product"
            && r.Property == "Suppliers"
            && r.Reason.Contains("junction entity"));
    }

    [Fact]
    public void TheCatalogNamesTheJunctionTableAndTheEntityIsSynthesized()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(ProductSource);
        parser.Parse(SupplierSource);

        CatalogCompletion.Complete(
            builder, new FakeCatalogReader(ProductsImage(), SuppliersImage(), JunctionImage()));
        builder.Build();

        // What the same catalog does for a Dapper source it now does for an EF Core one:
        // the source declares the collections, the schema the association.
        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");
        Assert.True(junction.IsJunctionTable);
        Assert.Equal("ProductSuppliers", junction.Table);
        Assert.Equal(2, junction.PrimaryKey!.Parts.Count);
        Assert.Equal(2, junction.Relations.Count);

        // Both collections hold the junction entity afterwards, as decision 005 rules.
        Assert.Equal("ProductSupplier", RelationOf(builder, "Product").TargetEntity);
        Assert.Equal("ProductSupplier", RelationOf(builder, "Supplier").TargetEntity);
    }

    [Fact]
    public void TheManyToManyReachesTheNHibernateMapping()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(ProductSource);
        parser.Parse(SupplierSource);

        CatalogCompletion.Complete(
            builder, new FakeCatalogReader(ProductsImage(), SuppliersImage(), JunctionImage()));
        var outputs = builder.Build();

        // The junction entity maps like any other: a composite identifier and two owning
        // references, read-only because their columns are the key's.
        var xml = outputs.Single(o =>
            o.ContentType == Model.ConversionContentType.XML && o.Content.Contains("<class name=\"ProductSupplier\""));
        Assert.Contains("table=\"ProductSuppliers\"", xml.Content);
        Assert.Contains(
            "<many-to-one name=\"Product\" class=\"Product\" column=\"ProductId\" insert=\"false\" update=\"false\" />",
            xml.Content);
        Assert.Contains(
            "<many-to-one name=\"Supplier\" class=\"Supplier\" column=\"SupplierId\" insert=\"false\" update=\"false\" />",
            xml.Content);
    }

    [Fact]
    public void ACollectionAnsweredByAReferenceStaysAOneToMany()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse("""
            public class Customer
            {
                [Key]
                public int CustomerId { get; set; }

                public List<Order> Orders { get; set; } = [];
            }
            """);
        parser.Parse("""
            public class Order
            {
                [Key]
                public int OrderId { get; set; }

                public int CustomerId { get; set; }

                public Customer Customer { get; set; }
            }
            """);

        builder.Build();

        var orders = RelationOf(builder, "Customer");
        Assert.Equal(Cardinality.OneToMany, orders.Cardinality);
        Assert.Equal(RelationRole.Inverse, orders.Role);

        var customer = RelationOf(builder, "Order");
        Assert.Equal(Cardinality.ManyToOne, customer.Cardinality);
        Assert.Equal(RelationRole.Owning, customer.Role);
    }

    [Fact]
    public void ACollectionNothingAnswersStaysAOneToMany()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        parser.Parse(ProductSource);
        parser.Parse("""
            public class Supplier
            {
                [Key]
                public int SupplierId { get; set; }
            }
            """);

        builder.Build();

        // EF Core puts the foreign key of such a one-to-many on the far entity as a shadow
        // property; the reading is unchanged and nothing is paired.
        Assert.Equal(Cardinality.OneToMany, RelationOf(builder, "Product").Cardinality);
        Assert.Empty(builder.EntityMaps.Single(em => em.Entity.Name == "Supplier").Relations);
    }

    [Fact]
    public void InversePropertyNamingACollectionMakesTheManyToMany()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        // Two pairs of collections between the same two entities: no convention can tell
        // which answers which, and [InverseProperty] is what EF Core answers with.
        parser.Parse("""
            using System.ComponentModel.DataAnnotations.Schema;

            public class Article
            {
                [Key]
                public int ArticleId { get; set; }

                [InverseProperty("Articles")]
                public List<Tag> Tags { get; set; } = [];

                [InverseProperty("ArchivedArticles")]
                public List<Tag> ArchivedTags { get; set; } = [];
            }
            """);
        parser.Parse("""
            using System.ComponentModel.DataAnnotations.Schema;

            public class Tag
            {
                [Key]
                public int TagId { get; set; }

                [InverseProperty("Tags")]
                public List<Article> Articles { get; set; } = [];

                [InverseProperty("ArchivedTags")]
                public List<Article> ArchivedArticles { get; set; } = [];
            }
            """);

        builder.Build();

        var article = builder.EntityMaps.Single(em => em.Entity.Name == "Article");
        Assert.All(article.Relations, r => Assert.Equal(Cardinality.ManyToMany, r.Cardinality));
        Assert.Equal(2, article.Relations.Count);
        Assert.Equal("Articles", article.Relations.Single(r => r.SourceNavigationProperty == "Tags").InverseRelationName);
    }

    [Fact]
    public void AnAmbiguousPairDerivesNothingAndIsRecorded()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        // The same two pairs without the annotation: EF Core refuses to build a model from
        // this source at all, so no shape is derived and the collections stay bare.
        parser.Parse("""
            public class Article
            {
                [Key]
                public int ArticleId { get; set; }

                public List<Tag> Tags { get; set; } = [];

                public List<Tag> ArchivedTags { get; set; } = [];
            }
            """);
        parser.Parse("""
            public class Tag
            {
                [Key]
                public int TagId { get; set; }

                public List<Article> Articles { get; set; } = [];
            }
            """);

        builder.Build();

        Assert.Empty(builder.EntityMaps.Single(em => em.Entity.Name == "Article").Relations);
        Assert.Empty(builder.EntityMaps.Single(em => em.Entity.Name == "Tag").Relations);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness
            && r.Category == MappingFactCategory.ForeignKeyColumns
            && r.Entity == "Article"
            && r.Property == "Tags"
            && r.Reason.Contains("InverseProperty"));
    }

    [Fact]
    public void ForeignKeyOnTheCollectionStatesAOneToManyOutright()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        // [ForeignKey] on a collection names the dependent's key properties, so the
        // annotation states the relation and the far side has nothing to add - not even
        // where a collection answers back.
        parser.Parse("""
            using System.ComponentModel.DataAnnotations.Schema;

            public class Product
            {
                [Key]
                public int ProductId { get; set; }

                [ForeignKey("ProductId")]
                public List<Supplier> Suppliers { get; set; } = [];
            }
            """);
        parser.Parse(SupplierSource);

        builder.Build();

        var suppliers = RelationOf(builder, "Product");
        Assert.Equal(Cardinality.OneToMany, suppliers.Cardinality);
        Assert.Equal("ProductId", Assert.Single(suppliers.ColumnPairs).Source.Property.Name);
    }

    [Fact]
    public void ATransientCollectionAnswersNothing()
    {
        var builder = new EFCoreEntityBuilder();
        var parser = new EFCoreEntityParser(builder);

        // [NotMapped] takes the property out of EF Core's model (decision 072), so it
        // founds no relationship and cannot be the far end of one either.
        parser.Parse(ProductSource);
        parser.Parse("""
            using System.ComponentModel.DataAnnotations.Schema;

            public class Supplier
            {
                [Key]
                public int SupplierId { get; set; }

                [NotMapped]
                public List<Product> Products { get; set; } = [];
            }
            """);

        builder.Build();

        Assert.Equal(Cardinality.OneToMany, RelationOf(builder, "Product").Cardinality);
        Assert.Empty(builder.EntityMaps.Single(em => em.Entity.Name == "Supplier").Relations);
    }
}
