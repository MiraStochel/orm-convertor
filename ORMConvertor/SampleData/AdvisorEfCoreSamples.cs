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
        """;

    public const string Query = """
        using Microsoft.EntityFrameworkCore;

        public static class MyQueries
        {
            public static IEnumerable<Customer> Query(DbContext ctx)
            {
                return ctx.Set<Customer>()
                          .Where(c => c.CreditLimit > 2000)
                          .ToList();
            }
        }
        """;
}

