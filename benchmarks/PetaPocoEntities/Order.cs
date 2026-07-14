using PetaPoco;

namespace PetaPocoEntities;

[TableName("Sales.Orders")]
public class Order
{
    public int OrderID { get; set; }

    public int CustomerID { get; set; }

    public int SalespersonPersonID { get; set; }

    public int? PickedByPersonID { get; set; }

    public int ContactPersonID { get; set; }

    public int? BackorderOrderID { get; set; }

    public DateTime OrderDate { get; set; }

    public DateTime ExpectedDeliveryDate { get; set; }

    public string? CustomerPurchaseOrderNumber { get; set; }

    public bool IsUndersupplyBackordered { get; set; }

    public string? Comments { get; set; }

    public string? DeliveryInstructions { get; set; }

    public string? InternalComments { get; set; }

    public DateTime? PickingCompletedWhen { get; set; }

    public int LastEditedBy { get; set; }

    public DateTime LastEditedWhen { get; set; }

    [Ignore]
    public List<OrderLine> OrderLines { get; set; } = [];
}
