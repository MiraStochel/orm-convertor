namespace NHibernateEntities;

public class Order
{
    public virtual int OrderID { get; set; }

    public virtual int CustomerID { get; set; }

    public virtual int SalespersonPersonID { get; set; }

    public virtual int? PickedByPersonID { get; set; }

    public virtual int ContactPersonID { get; set; }

    public virtual int? BackorderOrderID { get; set; }

    public virtual DateTime OrderDate { get; set; }

    public virtual DateTime ExpectedDeliveryDate { get; set; }

    public virtual string? CustomerPurchaseOrderNumber { get; set; }

    public virtual bool IsUndersupplyBackordered { get; set; }

    public virtual string? Comments { get; set; }

    public virtual string? DeliveryInstructions { get; set; }

    public virtual string? InternalComments { get; set; }

    public virtual DateTime? PickingCompletedWhen { get; set; }

    public virtual int LastEditedBy { get; set; }

    public virtual DateTime LastEditedWhen { get; set; }

    public virtual IList<OrderLine> OrderLines { get; set; } = [];
}
