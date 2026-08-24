using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;

namespace Tests.Catalog;

/// <summary>
/// The completion phase supplying unique constraints (decision 055): the catalog states
/// them by column, the model holds them by property, and identity is the set of properties
/// rather than the name. Judged over a fake catalog, like the rest of decision 015's
/// control side.
/// </summary>
public class CatalogUniqueConstraintTest
{
    private const string ProductSource = """
        public class Product
        {
            public int ProductId { get; set; }

            public string Sku { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;
        }
        """;

    private static TableImage ProductsImage(params UniqueConstraintImage[] uniqueConstraints) => new()
    {
        Schema = "dbo",
        Name = "Products",
        Columns =
        [
            new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
            new ColumnImage { Name = "Sku", Type = DatabaseType.VarChar, Length = 20, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, Length = 100, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["ProductId"],
        ForeignKeys = [],
        UniqueConstraints = uniqueConstraints,
    };

    private static EFCoreEntityBuilder ParseProduct()
    {
        var builder = new EFCoreEntityBuilder();
        new DapperEntityParser(builder).Parse(ProductSource);
        builder.AddTable("Products");
        return builder;
    }

    [Fact]
    public void TheCatalogSuppliesTheConstraintTranslatedFromColumnsToProperties()
    {
        var builder = ParseProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Sku", Columns = ["Sku"] })));

        var constraint = Assert.Single(builder.EntityMaps.Single().UniqueConstraints);

        Assert.Equal("UQ_Products_Sku", constraint.Name);
        Assert.Equal(["Sku"], constraint.PropertyNames);

        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property == "Sku");
    }

    [Fact]
    public void AMultiColumnConstraintKeepsTheOrderOfTheCatalog()
    {
        var builder = ParseProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_SkuName", Columns = ["Sku", "Name"] })));

        var constraint = Assert.Single(builder.EntityMaps.Single().UniqueConstraints);

        Assert.Equal(["Sku", "Name"], constraint.PropertyNames);

        // The record concerns the entity, not one of its properties.
        Assert.Contains(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied
            && r.Category == MappingFactCategory.UniqueConstraint
            && r.Property is null);
    }

    [Fact]
    public void AConstraintTheSourceAlreadyStatesIsNotSuppliedAgain()
    {
        var builder = ParseProduct();
        builder.AddUniqueConstraint("UQ_Products_Sku", ["Sku"]);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Sku", Columns = ["Sku"] })));

        Assert.Single(builder.EntityMaps.Single().UniqueConstraints);
        Assert.DoesNotContain(builder.Records, r =>
            r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.UniqueConstraint);
    }

    [Fact]
    public void ADifferingNameOverTheSameSetIsAConflictTheSourceWins()
    {
        var builder = ParseProduct();
        builder.AddUniqueConstraint("UQ_FromTheSource", ["Sku"]);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Sku", Columns = ["Sku"] })));

        var constraint = Assert.Single(builder.EntityMaps.Single().UniqueConstraints);

        // Rule E9: the source outranks the catalog, and the catalog's name is reported.
        Assert.Equal("UQ_FromTheSource", constraint.Name);

        var conflict = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Conflict && r.Category == MappingFactCategory.UniqueConstraint);

        Assert.Contains("UQ_Products_Sku", conflict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamelessConstraintOfTheSourceIsNamedByTheCatalog()
    {
        var builder = ParseProduct();
        builder.AddUniqueConstraint(null, ["Sku"]);

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Sku", Columns = ["Sku"] })));

        var constraint = Assert.Single(builder.EntityMaps.Single().UniqueConstraints);

        Assert.Equal("UQ_Products_Sku", constraint.Name);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Conflict);
    }

    [Fact]
    public void AConstraintOverAColumnNoPropertyMapsIsNotSupplied()
    {
        var builder = ParseProduct();

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Barcode", Columns = ["Sku", "Barcode"] })));

        // Inventing the member would put into the class what the source never declared,
        // the same rule the primary key follows.
        Assert.Empty(builder.EntityMaps.Single().UniqueConstraints);

        var record = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Incompleteness && r.Category == MappingFactCategory.UniqueConstraint);

        Assert.Contains("Barcode", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ATargetThatCannotExpressTheFactNeverAsksTheCatalogForIt()
    {
        // Dapper marks the category NotExpressible, so it is outside the demand and the
        // constraint of the catalog is never read into the model (decision 015).
        var builder = new DapperEntityBuilder();
        new DapperEntityParser(builder).Parse(ProductSource);
        builder.AddTable("Products");

        CatalogCompletion.Complete(builder, new FakeCatalogReader(
            ProductsImage(new UniqueConstraintImage { Name = "UQ_Products_Sku", Columns = ["Sku"] })));

        Assert.Empty(builder.EntityMaps.Single().UniqueConstraints);
    }
}
