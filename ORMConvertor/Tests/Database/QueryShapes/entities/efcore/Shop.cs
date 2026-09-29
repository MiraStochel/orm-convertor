// The domain of the query-shape matrices as EF Core states it: the table on the class,
// [Key] on a one-part key and [PrimaryKey] in the order of the parts on a composite one.
// ShopOrders has a two-part key, ShopOrderLines a three-part one whose leading parts are the
// foreign key to it, ShopOrderLineAllocations a four-part one - the shape the read-only
// fixture of this domain has, so that the joins of the matrices run over two and three columns.
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Shop;

[Table("ShopCustomers", Schema = "{{schema}}")]
public class ShopCustomer
{
    [Key]
    public int CustomerId { get; set; }
    public string Name { get; set; }
    public string? Notes { get; set; }
}

[Table("ShopOrders", Schema = "{{schema}}")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId))]
public class ShopOrder
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
    public bool IsCancelled { get; set; }
}

[Table("ShopOrderLines", Schema = "{{schema}}")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId), nameof(LineNumber))]
public class ShopOrderLine
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int ProductId { get; set; }
    public string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[Table("ShopOrderLineAllocations", Schema = "{{schema}}")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId), nameof(LineNumber), nameof(AllocationId))]
public class ShopOrderLineAllocation
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int AllocationId { get; set; }
    public int AllocatedQuantity { get; set; }
    public string? Notes { get; set; }
}

[Table("ShopProducts", Schema = "{{schema}}")]
public class ShopProduct
{
    [Key]
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsDiscontinued { get; set; }
}
