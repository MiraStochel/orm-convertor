using PetaPoco;

namespace PetaPocoEntities;

[TableName("Sales.CustomerTransactions")]
public class CustomerTransaction
{
    public int CustomerTransactionID { get; set; }

    public int CustomerID { get; set; }

    public DateTime TransactionDate { get; set; }

    public decimal TransactionAmount { get; set; }
}
