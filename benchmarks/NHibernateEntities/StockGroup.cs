namespace NHibernateEntities;

public class StockGroup
{
    public virtual int StockGroupID { get; set; }

    public virtual required string StockGroupName { get; set; }

    public virtual IList<StockItem> StockItems { get; set; } = [];
}
