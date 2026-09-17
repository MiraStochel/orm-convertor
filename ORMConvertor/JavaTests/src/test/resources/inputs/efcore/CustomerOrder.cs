// The composite key as EF Core states it: [PrimaryKey] on the class, in the order of the
// key. EF Core generates no value for the parts of a composite key, so no strategy is
// stated and none is needed.
namespace Shop;

using System;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

[Table("Orders", Schema = "{{schema}}")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId))]
public class CustomerOrder
{
    [Column("CompanyId")]
    public int CompanyId { get; set; }

    [Column("OrderId")]
    public int OrderId { get; set; }

    [Column("CustomerId")]
    public int CustomerId { get; set; }

    [Column("OrderDate")]
    public DateOnly OrderDate { get; set; }

    [Column("IsCancelled")]
    public bool IsCancelled { get; set; }
}
