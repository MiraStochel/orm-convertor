namespace NHibernateEntities;

public class PurchaseOrderUpdate
{
    public virtual int OrderID { get; set; }

    public virtual string? Description { get; set; }

    public virtual int Quantity { get; set; }

    public virtual decimal UnitPrice { get; set; }

    public virtual decimal TaxRate { get; set; }

    public virtual decimal TotalIncludingTax { get; set; }
}
