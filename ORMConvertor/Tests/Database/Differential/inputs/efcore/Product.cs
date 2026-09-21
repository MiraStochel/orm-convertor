// The EF Core source of the differential matrix (decision 089). Every name is stated, so
// the row needs no catalog; DatabaseGeneratedOption.None is not decoration - without it a
// single int key is EF Core's convention for a store-generated value, and Products carries
// no IDENTITY, so verification would be writing about a column the fixture does not have.
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Shop;

[Table("DifferentialProducts", Schema = "{{schema}}")]
public class DifferentialProduct
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("ProductId")]
    public int ProductId { get; set; }

    [Column("ProductName")]
    public string ProductName { get; set; }

    [Column("Sku")]
    public string Sku { get; set; }

    [Column("UnitPrice")]
    public decimal UnitPrice { get; set; }

    [Column("Weight")]
    public double? Weight { get; set; }

    [Column("IsDiscontinued")]
    public bool IsDiscontinued { get; set; }
}
