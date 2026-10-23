using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop;

[Table("DifferentialProducts", Schema = "{{schema}}")]
public class DifferentialProduct
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public double? Weight { get; set; }

    public bool IsDiscontinued { get; set; }
