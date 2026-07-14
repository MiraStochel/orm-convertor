using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

[Table("Customers", Schema = "Sales")]
public class Customer
{
    [Key]
    public int CustomerID { get; set; }

    public required string CustomerName { get; set; }

    public DateTime AccountOpenedDate { get; set; }

    public decimal? CreditLimit { get; set; }

    public List<CustomerTransaction> Transactions { get; set; } = [];
}
