namespace NHibernateEntities;

public class PurchaseOrder
{
    public virtual int PurchaseOrderID { get; set; }

    public virtual int SupplierID { get; set; }

    public virtual DateTime OrderDate { get; set; }

    public virtual int DeliveryMethodID { get; set; }

    public virtual int ContactPersonID { get; set; }

    public virtual DateTime? ExpectedDeliveryDate { get; set; }

    public virtual string? SupplierReference { get; set; }

    public virtual bool IsOrderFinalized { get; set; }

    public virtual string? Comments { get; set; }

    public virtual string? InternalComments { get; set; }

    public virtual int LastEditedBy { get; set; }

    public virtual DateTime LastEditedWhen { get; set; }
}
