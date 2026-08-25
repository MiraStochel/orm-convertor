using DatabaseCatalog;
using Model;
using OrmConvertor;

namespace Tests.Verification;

/// <summary>
/// The Dapper source of the many-to-many scenario (decisions 005, 015 and 016): both
/// classes declare only their collections, the schema owns the junction table, and the
/// conversion has to come out with a synthesized junction entity. Shared between the
/// acceptance tests (third level) and the persistence tests (fourth level), which judge
/// the same artifacts.
/// </summary>
internal static class DapperJunctionSourceEntities
{
    public const string AssemblyName = "DapperJunctionEntities";

    public const string SupplierSource = """
        namespace DapperJunctionEntities;

        public class Supplier
        {
            public int SupplierId { get; set; }

            public string SupplierName { get; set; } = string.Empty;

            public List<Product> Products { get; set; } = [];
        }
        """;

    public const string ProductSource = """
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

    /// <summary>
    /// Runs the whole pipeline the way the orchestration runs it, against the test
    /// database. The junction exists only in the catalog, so every caller needs the
    /// fixture's reader and skips with the rest when no database is configured.
    /// </summary>
    public static ConversionResult Convert(ORMEnum target, ICatalogReader catalogReader)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = SupplierSource },
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = ProductSource },
            ],
            catalogReader);
}
