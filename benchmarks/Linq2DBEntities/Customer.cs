namespace linq2dbEntities;

public class Customer
{
    public int CustomerID { get; set; }

    public required string CustomerName { get; set; }

    public DateTime AccountOpenedDate { get; set; }

    public decimal? CreditLimit { get; set; }

    public List<CustomerTransaction> Transactions { get; set; } = [];
}
