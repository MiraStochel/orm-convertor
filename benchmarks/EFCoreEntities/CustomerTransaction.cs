using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCoreEntities;

[Table("CustomerTransactions", Schema = "Sales")]
public class CustomerTransaction
{
    [Key]
    public int CustomerTransactionID { get; set; }

    [ForeignKey(nameof(Customer))]
    public int CustomerID { get; set; }

    public DateTime TransactionDate { get; set; }

    public decimal TransactionAmount { get; set; }
}
