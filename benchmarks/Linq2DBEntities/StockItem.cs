namespace linq2dbEntities;

public class StockItem
{
    public int StockItemID { get; set; }

    public required string StockItemName { get; set; }

    public int SupplierID { get; set; }

    public string? Brand { get; set; }

    public string? Size { get; set; }

    public List<StockItemStockGroup> StockGroups { get; set; } = [];
}
