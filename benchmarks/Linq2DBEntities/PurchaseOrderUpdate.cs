namespace linq2dbEntities;

public class PurchaseOrderUpdate
{
    public int OrderID { get; set; }

    public string? Description { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TaxRate { get; set; }

    public decimal TotalIncludingTax { get; set; }
}
