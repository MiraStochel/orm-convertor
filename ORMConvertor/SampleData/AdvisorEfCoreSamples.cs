namespace SampleData;

public static class AdvisorEfCoreSamples
{
    public const string Entity = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key] public int CustomerID { get; set; }
            public required string CustomerName { get; set; }
            public DateTime AccountOpenedDate { get; set; }
            public decimal? CreditLimit { get; set; }
        }

        
        [Table("OrderLines", Schema = "Sales")]
        public class OrderLine
        {
            [Key]
            public int OrderLineID { get; set; }
            public int OrderID { get; set; }
            public required string Description { get; set; }
            public int Quantity { get; set; }
            public decimal? UnitPrice { get; set; }
            public decimal TaxRate { get; set; }
        }
        """;

    public const string Query1 = """
        public static class MyQueries
        {
            public static List<Customer> Query1(DbContext ctx)
            {
                return ctx.Set<Customer>()
                    .Where(c => c.CreditLimit > 500)
                    .OrderByDescending(c => c.AccountOpenedDate)
                    .ThenBy(c => c.CustomerName)
                    .ToList();
            }
        }
        """;

    public const string Query2 = """
        public static class MyQueries
        {
            public static List<OrderLine> Query2(DbContext ctx)
            {
                return ctx.Set<OrderLine>()
                    .Where(o => o.TaxRate == 15)
                    .Where(o => o.UnitPrice != null)
                    .ToList();
            }
        }
        """;
}

