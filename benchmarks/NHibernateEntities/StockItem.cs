namespace NHibernateEntities;

public class StockItem
{
    public virtual int StockItemID { get; set; }

    public virtual required string StockItemName { get; set; }

    public virtual int SupplierID { get; set; }

    public virtual string? Brand { get; set; }

    public virtual string? Size { get; set; }

    public virtual IList<StockGroup> StockGroups { get; set; } = [];
}
