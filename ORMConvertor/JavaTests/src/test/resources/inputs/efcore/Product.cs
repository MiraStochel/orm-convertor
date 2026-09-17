// An EF Core source unit of the Java suite (decision 078): the same Products table of the
// fixture schema, stated the way EF Core states it. DatabaseGeneratedOption.None is not
// decoration - without it a single int key is EF Core's convention for a store-generated
// value, which reaches Hibernate as AUTO and, per the implementation profile, as a
// sequence this schema does not have.
namespace Shop;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("Products", Schema = "{{schema}}")]
public class Product
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

    [Column("IsDiscontinued")]
    public bool IsDiscontinued { get; set; }
}
