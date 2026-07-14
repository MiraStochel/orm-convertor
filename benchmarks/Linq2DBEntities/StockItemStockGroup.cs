namespace linq2dbEntities;
public class StockItemStockGroup
{
    public int StockItemStockGroupID { get; set; }

    public int StockItemID { get; set; }

    public int StockGroupID { get; set; }

    public required StockItem StockItem { get; set; }

    public required StockGroup StockGroup { get; set; }
}
