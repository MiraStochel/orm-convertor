using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

[Table("OrderLines", Schema = "Sales")]
public class OrderLine
{
    [Key]
    public int OrderLineID { get; set; }

    public int OrderID { get; set; }

    public int StockItemID { get; set; }

    public required string Description { get; set; }

    public int PackageTypeID { get; set; }

    public int Quantity { get; set; }

    public decimal? UnitPrice { get; set; }

    public decimal TaxRate { get; set; }

    public int PickedQuantity { get; set; }

    public DateTime? PickingCompletedWhen { get; set; }

    public int LastEditedBy { get; set; }

    public DateTime LastEditedWhen { get; set; }
}
