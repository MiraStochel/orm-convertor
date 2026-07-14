using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

public class PurchaseOrderUpdate
{
    [Column("WWI Order ID")]
    public int OrderID { get; set; }

    [Column("Description")]
    public string? Description { get; set; }

    [Column("Quantity")]
    public int Quantity { get; set; }

    [Column("Unit Price")]
    public decimal UnitPrice { get; set; }

    [Column("Tax Rate")]
    public decimal TaxRate { get; set; }

    [Column("Total Including Tax")]
    public decimal TotalIncludingTax { get; set; }
}
