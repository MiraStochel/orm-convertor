using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

[Table("StockItems", Schema = "Warehouse")]
public class StockItem
{
    [Key]
    public int StockItemID { get; set; }

    public required string StockItemName { get; set; }

    public int SupplierID { get; set; }

    public string? Brand { get; set; }

    public string? Size { get; set; }

     public ICollection<StockGroup> StockGroups { get; set; } = new HashSet<StockGroup>();
}
