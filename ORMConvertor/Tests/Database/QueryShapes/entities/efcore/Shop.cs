// The domain of the query-shape matrices as EF Core states it: the table on the class,
// [Key] on a one-part key and [PrimaryKey] in the order of the parts on a composite one.
// Orders has a two-part key, OrderLines a three-part one whose leading parts are the
// foreign key to Orders, OrderLineAllocations a four-part one - the shape the shared
// fixture schema has, so that the joins of the matrices run over two and three columns.
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Shop;

[Table("Customers", Schema = "Sales")]
public class Customer
{
    [Key]
    public int CustomerId { get; set; }
    public string Name { get; set; }
    public string? Notes { get; set; }
}

[Table("Orders", Schema = "Sales")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId))]
public class CustomerOrder
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
    public bool IsCancelled { get; set; }
}

[Table("OrderLines", Schema = "Sales")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId), nameof(LineNumber))]
public class OrderLine
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int ProductId { get; set; }
    public string Description { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

[Table("OrderLineAllocations", Schema = "Sales")]
[PrimaryKey(nameof(CompanyId), nameof(OrderId), nameof(LineNumber), nameof(AllocationId))]
public class OrderLineAllocation
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int AllocationId { get; set; }
    public int AllocatedQuantity { get; set; }
    public string? Notes { get; set; }
}

[Table("Products", Schema = "Sales")]
public class Product
{
    [Key]
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsDiscontinued { get; set; }
}
