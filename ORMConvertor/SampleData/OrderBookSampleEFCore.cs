namespace SampleData;

/// <summary>
/// The order book: the larger .NET-to-Java example of the explanatory page (decision 099),
/// four EF Core entities and six LINQ queries over them. It is a domain rather than a class
/// on purpose - the phases that exist because a project has several entities never run on a
/// single one: a composite key one part of which is also a foreign key, which a JPA target
/// renders with a key class of its own (decisions 006 and 077), foreign keys paired across
/// the entities of the conversion (decisions 001 and 012),
/// and several query units in one conversion, each record naming the file it came from
/// (decision 066).
///
/// The tables live in a schema of their own, Ordering, which the sample database of the
/// container does not have: on an instance with a catalog the records then say the table was
/// not found, instead of mixing in the facts of another domain that happens to share a name.
/// </summary>
public static class OrderBookSampleEFCore
{
    /// <summary>
    /// A unique index with a name of its own, a non-national column, a nullable decimal with
    /// precision, a row version, and a property the database never sees.
    /// </summary>
    public const string Customer = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace OrderBook;

        [Table("Customers", Schema = "Ordering")]
        [Index(nameof(Email), IsUnique = true, Name = "UQ_Customers_Email")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }

            [MaxLength(100)]
            public required string Name { get; set; }

            [MaxLength(254)]
            [Unicode(false)]
            public required string Email { get; set; }

            [Precision(18, 2)]
            public decimal? CreditLimit { get; set; }

            public DateOnly CustomerSince { get; set; }

            [Timestamp]
            public byte[] RowVersion { get; set; } = [];

            [NotMapped]
            public bool IsPreferred { get; set; }

            public List<SalesOrder> Orders { get; set; } = [];
        }
        """;

    /// <summary>
    /// A foreign key stated on the navigation, a column named differently from its property,
    /// fractional seconds, and a non-unique index - a performance artifact the representation
    /// does not carry, so its record is the one loss the example shows on purpose.
    /// </summary>
    public const string SalesOrder = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace OrderBook;

        [Table("SalesOrders", Schema = "Ordering")]
        [Index(nameof(OrderNumber), IsUnique = true)]
        [Index(nameof(OrderedAt))]
        public class SalesOrder
        {
            [Key]
            public int SalesOrderId { get; set; }

            [MaxLength(20)]
            [Unicode(false)]
            public required string OrderNumber { get; set; }

            public int CustomerId { get; set; }

            [ForeignKey(nameof(CustomerId))]
            public required Customer Customer { get; set; }

            [Precision(3)]
            public DateTime OrderedAt { get; set; }

            [MaxLength(12)]
            [Unicode(false)]
            public required string Status { get; set; }

            [Column("ShipToCity")]
            [MaxLength(60)]
            public string? City { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }
        """;

    /// <summary>
    /// The composite key: the order the line belongs to and its number within it. The first
    /// part is at the same time the foreign key to the order, which is what makes the target
    /// hand the column to the key and keep the relation from writing it.
    /// </summary>
    public const string OrderLine = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace OrderBook;

        [Table("OrderLines", Schema = "Ordering")]
        [PrimaryKey(nameof(SalesOrderId), nameof(LineNumber))]
        public class OrderLine
        {
            public int SalesOrderId { get; set; }

            public short LineNumber { get; set; }

            [ForeignKey(nameof(SalesOrderId))]
            public required SalesOrder Order { get; set; }

            public int ProductId { get; set; }

            [ForeignKey(nameof(ProductId))]
            public required Product Product { get; set; }

            public int Quantity { get; set; }

            [Precision(18, 2)]
            public decimal UnitPrice { get; set; }

            [Precision(5, 2)]
            public decimal DiscountPercent { get; set; }
        }
        """;

    /// <summary>A key the application assigns, stated by the annotation that says so.</summary>
    public const string Product = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace OrderBook;

        [Table("Products", Schema = "Ordering")]
        [Index(nameof(Sku), IsUnique = true)]
        public class Product
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.None)]
            public int ProductId { get; set; }

            [MaxLength(32)]
            [Unicode(false)]
            public required string Sku { get; set; }

            [MaxLength(200)]
            public required string Name { get; set; }

            [Precision(18, 2)]
            public decimal ListPrice { get; set; }

            public bool IsDiscontinued { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }
        """;

    /// <summary>Parameters from the enclosing method, a list of values, two orderings and a page.</summary>
    public const string OpenOrdersQuery = """
        public List<SalesOrder> OpenOrders(int customerId, DateTime placedAfter, int skip, int take)
        {
            return ctx.SalesOrders
                .Where(o => o.CustomerId == customerId
                         && o.OrderedAt >= placedAfter
                         && new[] { "New", "Paid", "Packed" }.Contains(o.Status))
                .OrderByDescending(o => o.OrderedAt)
                .ThenBy(o => o.OrderNumber)
                .Skip(skip)
                .Take(take)
                .ToList();
        }
        """;

    /// <summary>A filter, a grouping, a filter over the groups and four aggregates.</summary>
    public const string BestSellersQuery = """
        public void BestSellers(int minimumQuantity)
        {
            var rows = ctx.OrderLines
                .Where(l => l.DiscountPercent < 50)
                .GroupBy(l => l.ProductId)
                .Where(g => g.Sum(l => l.Quantity) >= minimumQuantity)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Quantity = g.Sum(l => l.Quantity),
                    Lines = g.Count(),
                    HighestPrice = g.Max(l => l.UnitPrice),
                })
                .ToList();
        }
        """;

    /// <summary>A disjunction over a null test beside a correlated NOT EXISTS.</summary>
    public const string DormantCustomersQuery = """
        public List<Customer> DormantCustomers(DateTime since)
        {
            return ctx.Customers
                .Where(c => (c.CreditLimit == null || c.CreditLimit > 0)
                         && !ctx.SalesOrders.Any(o => o.CustomerId == c.CustomerId && o.OrderedAt >= since))
                .OrderBy(c => c.Name)
                .ToList();
        }
        """;

    /// <summary>An IN over a subquery whose own filter takes a collection parameter.</summary>
    public const string OrdersWithProductsQuery = """
        public List<SalesOrder> OrdersWithProducts(List<int> productIds)
        {
            return ctx.SalesOrders
                .Where(o => ctx.OrderLines
                    .Where(l => productIds.Contains(l.ProductId))
                    .Select(l => l.SalesOrderId)
                    .Contains(o.SalesOrderId))
                .OrderBy(o => o.SalesOrderId)
                .ToList();
        }
        """;

    /// <summary>A scalar subquery: the average price of the products still on sale.</summary>
    public const string PricedAboveAverageQuery = """
        public List<Product> PricedAboveAverage()
        {
            return ctx.Products
                .Where(p => p.IsDiscontinued == false
                         && p.ListPrice > ctx.Products
                                .Where(x => x.IsDiscontinued == false)
                                .Average(x => x.ListPrice))
                .OrderByDescending(p => p.ListPrice)
                .ToList();
        }
        """;

    /// <summary>A union of two projections, each with a parameter of its own.</summary>
    public const string MailingListQuery = """
        public void MailingList(decimal minimumCreditLimit, DateOnly customerBefore)
        {
            var addresses = ctx.Customers
                .Where(c => c.CreditLimit >= minimumCreditLimit)
                .Select(c => new { Email = c.Email })
                .Union(ctx.Customers
                    .Where(c => c.CustomerSince < customerBefore)
                    .Select(c => new { Email = c.Email }))
                .ToList();
        }
        """;
}
