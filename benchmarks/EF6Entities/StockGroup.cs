using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EF6Entities;

[Table("StockGroups", Schema = "Warehouse")]
public class StockGroup
{
    [Key]
    public int StockGroupID { get; set; }

    public required string StockGroupName { get; set; }

    public ICollection<StockItem> StockItems { get; set; } = new HashSet<StockItem>();
}
