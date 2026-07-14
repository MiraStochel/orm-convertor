using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

[Table("PurchaseOrders", Schema = "Purchasing")]
public class PurchaseOrder
{
    [Key]
    public int PurchaseOrderID { get; set; }

    public int SupplierID { get; set; }

    public DateTime OrderDate { get; set; }

    public int DeliveryMethodID { get; set; }

    public int ContactPersonID { get; set; }

    public DateTime? ExpectedDeliveryDate { get; set; }

    public string? SupplierReference { get; set; }

    public bool IsOrderFinalized { get; set; }

    public string? Comments { get; set; }

    public string? InternalComments { get; set; }

    public int LastEditedBy { get; set; }

    public DateTime LastEditedWhen { get; set; }
}
