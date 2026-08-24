using DatabaseCatalog;
using Model;
using OrmConvertor;

namespace Tests.Verification;

/// <summary>
/// The Dapper source of the F6 scenario: entities carrying nothing but property names and
/// language types, matching the tables of the schema the tests own (decision 016). Table
/// names, column facts, keys and foreign keys all have to come out of the catalog
/// (decision 015) - the source has no way to state any of them.
/// </summary>
internal static class DapperSourceEntities
{
    public const string AssemblyName = "DapperEntities";

    public const string CustomerSource = """
        namespace DapperEntities;

        public class Customer
        {
            public int CustomerId { get; set; }

            public string Name { get; set; } = string.Empty;

            public string? Notes { get; set; }

            public List<Order> Orders { get; set; } = [];
        }
        """;

    public const string OrderSource = """
        namespace DapperEntities;

        public class Order
        {
            public int CompanyId { get; set; }

            public int OrderId { get; set; }

            public int CustomerId { get; set; }

            public DateTime OrderDate { get; set; }

            public DateTime PlacedAt { get; set; }

            public bool IsCancelled { get; set; }

            public Guid? ExternalRef { get; set; }

            public Customer Customer { get; set; } = null!;

            public List<OrderLine> OrderLines { get; set; } = [];
        }
        """;

    public const string OrderLineSource = """
        namespace DapperEntities;

        public class OrderLine
        {
            public int CompanyId { get; set; }

            public int OrderId { get; set; }

            public int LineNo { get; set; }

            public int ProductId { get; set; }

            public string Description { get; set; } = string.Empty;

            public int Quantity { get; set; }

            public decimal UnitPrice { get; set; }

            public Order Order { get; set; } = null!;
        }
        """;

    /// <summary>
    /// Runs the whole pipeline - parsing, catalog completion, generation - the way the
    /// orchestration runs it, against the test database. The reader comes from the
    /// fixture, so its cache spans the collection and eight tests re-reading the same
    /// fixed schema cost one set of catalog statements.
    /// </summary>
    public static ConversionResult Convert(ORMEnum target, ICatalogReader catalogReader)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = CustomerSource },
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = OrderSource },
                new ConversionSource { ContentType = ConversionContentType.CSharpEntity, Content = OrderLineSource },
            ],
            catalogReader);
}
