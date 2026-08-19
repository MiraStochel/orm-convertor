using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Catalog;

/// <summary>
/// N:M detection over the catalog (decisions 005 and 015): a junction-shaped table
/// nobody's artifact expresses invokes the standard junction synthesis - through the
/// collection navigations the source declares - and an entity mapped to such a table gets
/// the junction flag. No member is invented: without a collection navigation the junction
/// table is reported, not turned into relations.
/// </summary>
public class CatalogManyToManyDetectionTest
{
    private const string SupplierSource = """
        namespace DapperEntities;

        public class Supplier
        {
            public int SupplierId { get; set; }

            public string SupplierName { get; set; } = string.Empty;

            public List<Product> Products { get; set; } = [];
        }
        """;

    private const string ProductSource = """
        namespace DapperEntities;

        public class Product
        {
            public int ProductId { get; set; }

            public string ProductName { get; set; } = string.Empty;

            public List<Supplier> Suppliers { get; set; } = [];
        }
        """;

    private static TableImage SuppliersImage(params ForeignKeyImage[] foreignKeys) => new()
    {
        Schema = "sales",
        Name = "Suppliers",
        Columns =
        [
            new ColumnImage { Name = "SupplierId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "SupplierName", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["SupplierId"],
        ForeignKeys = foreignKeys,
    };

    private static TableImage ProductsImage(params ForeignKeyImage[] foreignKeys) => new()
    {
        Schema = "sales",
        Name = "Products",
        Columns =
        [
            new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "ProductName", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["ProductId"],
        ForeignKeys = foreignKeys,
    };

    private static TableImage JunctionImage() => new()
    {
        Schema = "sales",
        Name = "ProductSuppliers",
        Columns =
        [
            new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "SupplierId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "SupplierSku", Type = DatabaseType.VarChar, IsUnicode = false, Length = 32, IsNullable = true, IsIdentity = false },
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

    private static NHibernateEntityBuilder ParseSupplierAndProduct()
    {
        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(SupplierSource);
        parser.Parse(ProductSource);
        return builder;
    }

    [Fact]
    public void SynthesizesTheManyToManyFromACatalogJunctionTable()
    {
        var builder = ParseSupplierAndProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(SuppliersImage(), ProductsImage(), JunctionImage()));

        // The source declares the collections, the schema the association: both sides get
        // a many-to-many with the junction table's facts, and each carries its origin.
        var supplier = builder.EntityMaps.Single(em => em.Entity.Name == "Supplier");
        var relation = Assert.Single(supplier.Relations);
        Assert.Equal(Cardinality.ManyToMany, relation.Cardinality);
        Assert.Equal("Product", relation.TargetEntity);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Entity == "Supplier" && r.Property == "Products");

        // The standard synthesis then builds the junction entity before generation.
        builder.Build();

        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");
        Assert.True(junction.IsJunctionTable);
        Assert.Equal("ProductSuppliers", junction.Table);
        Assert.Equal("sales", junction.Schema);
        Assert.Equal(2, junction.PrimaryKey!.Parts.Count);
        Assert.Equal(2, junction.Relations.Count);

        Assert.Equal(Cardinality.OneToMany, relation.Cardinality);
        Assert.Equal("ProductSupplier", relation.TargetEntity);
    }

    [Fact]
    public void PayloadColumnsOfTheJunctionTableAreReportedNotGenerated()
    {
        var builder = ParseSupplierAndProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(SuppliersImage(), ProductsImage(), JunctionImage()));
        builder.Build();

        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");
        Assert.DoesNotContain(junction.PropertyMaps, pm => pm.Property.Name == "SupplierSku");
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("SupplierSku"));
    }

    [Fact]
    public void MarksAConversionEntityMappedToAJunctionTable()
    {
        const string junctionEntitySource = """
            namespace DapperEntities;

            public class ProductSupplier
            {
                public int ProductId { get; set; }

                public int SupplierId { get; set; }
            }
            """;

        var builder = new NHibernateEntityBuilder();
        new DapperEntityParser(builder).Parse(junctionEntitySource);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(JunctionImage()));

        var em = builder.EntityMaps.Single();
        Assert.True(em.IsJunctionTable);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Reason.Contains("junction table"));
    }

    [Fact]
    public void WithoutACollectionNavigationTheJunctionIsReportedNotInvented()
    {
        const string bareSupplier = """
            namespace DapperEntities;

            public class Supplier
            {
                public int SupplierId { get; set; }
            }
            """;

        const string bareProduct = """
            namespace DapperEntities;

            public class Product
            {
                public int ProductId { get; set; }
            }
            """;

        var builder = new NHibernateEntityBuilder();
        var parser = new DapperEntityParser(builder);
        parser.Parse(bareSupplier);
        parser.Parse(bareProduct);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(SuppliersImage(), ProductsImage(), JunctionImage()));

        Assert.All(builder.EntityMaps, em => Assert.Empty(em.Relations));
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("collection navigation"));
    }

    [Fact]
    public void SuppliesTheTableOfAStatedManyToMany()
    {
        // The source states the many-to-many but not its table - an NHibernate
        // <many-to-many> without the table attribute; the catalog fills the gap and the
        // synthesis, which declined before, succeeds.
        var builder = ParseSupplierAndProduct();
        builder.EntityMap = builder.EntityMaps.Single(em => em.Entity.Name == "Supplier");
        builder.AddForeignKey(
            Cardinality.ManyToMany, "Products", "Product",
            foreignKeyColumns: ["SupplierId"],
            junction: new AbstractWrappers.JunctionFacts(null, null, ["ProductId"]));

        CatalogCompletion.Complete(builder, new FakeCatalogReader(SuppliersImage(), ProductsImage(), JunctionImage()));
        builder.Build();

        var junction = builder.EntityMaps.Single(em => em.Entity.Name == "ProductSupplier");
        Assert.Equal("ProductSuppliers", junction.Table);
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Reason.Contains("junction table"));
    }

    [Fact]
    public void ADirectForeignKeyMakesTheCollectionAmbiguous()
    {
        var products = ProductsImage(new ForeignKeyImage
        {
            Name = "FK_Products_Suppliers",
            ReferencedSchema = "sales",
            ReferencedTable = "Suppliers",
            Columns = [new ForeignKeyColumn("SupplierId", "SupplierId")],
        });

        var builder = ParseSupplierAndProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(SuppliersImage(), products, JunctionImage()));

        // Both a junction table and a direct foreign key link the two entities; deriving
        // either reading - the many-to-many over the junction, or the inverse one-to-many
        // over the direct key - would be a guess, so neither is derived and the state is reported.
        Assert.All(builder.EntityMaps, em => Assert.Empty(em.Relations));
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("ambiguous"));
    }
}
