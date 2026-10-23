using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop;

[Table("DifferentialProducts", Schema = "{{schema}}")]
public class DifferentialProduct
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int ProductId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    [MaxLength(32)]
    [Column(TypeName = "varchar(32)")]
    public string Sku { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal UnitPrice { get; set; }

    public double? Weight { get; set; }

    public bool IsDiscontinued { get; set; }
}
