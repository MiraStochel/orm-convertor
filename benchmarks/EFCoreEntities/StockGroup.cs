using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EFCoreEntities;

[Table("StockGroups", Schema = "Warehouse")]
public class StockGroup
{
    [Key]
    public int StockGroupID { get; set; }

    public required string StockGroupName { get; set; }

    public List<StockItem> StockItems { get; set; } = [];
}
