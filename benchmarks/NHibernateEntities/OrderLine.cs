namespace NHibernateEntities;

public class OrderLine
{
    public virtual int OrderLineID { get; set; }

    public virtual int OrderID { get; set; }

    public virtual int StockItemID { get; set; }

    public virtual required string Description { get; set; }

    public virtual int PackageTypeID { get; set; }

    public virtual int Quantity { get; set; }

    public virtual decimal? UnitPrice { get; set; }

    public virtual decimal TaxRate { get; set; }

    public virtual int PickedQuantity { get; set; }

    public virtual DateTime? PickingCompletedWhen { get; set; }

    public virtual int LastEditedBy { get; set; }

    public virtual DateTime LastEditedWhen { get; set; }
}
