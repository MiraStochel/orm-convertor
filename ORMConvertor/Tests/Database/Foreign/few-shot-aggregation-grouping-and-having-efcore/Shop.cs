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

[Table("ShopDepartments", Schema = "{{schema}}")]
public class ShopDepartment
{
    [Key]
    public int DepartmentId { get; set; }
    public int? ParentDepartmentId { get; set; }
    public string Name { get; set; }
}

[Table("ShopProductLinks", Schema = "{{schema}}")]
public class ShopProductLink
{
    [Key]
    public int LinkId { get; set; }
    public int FromProductId { get; set; }
    public int ToProductId { get; set; }
}
