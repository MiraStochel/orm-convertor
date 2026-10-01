// The domain of the query-shape matrices as Dapper states it: plain classes, no attribute,
// no key - every mapping fact is the catalog's to supply (F6). The seven classes carry
// the prefix Shop, so that the naming rule of decision 050 finds their tables - ShopOrders
// from ShopOrder - and finds them once: the fixture schema holds Orders, OrderLines and
// Products of its own. Read by both test suites from this one file.
using System;

namespace Shop;

public class ShopCustomer
{
    public int CustomerId { get; set; }
    public string Name { get; set; }
    public string? Notes { get; set; }
}

public class ShopOrder
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
    public bool IsCancelled { get; set; }
}

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

public class ShopOrderLineAllocation
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int AllocationId { get; set; }
    public int AllocatedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class ShopProduct
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsDiscontinued { get; set; }
}

public class ShopDepartment
{
    public int DepartmentId { get; set; }
    public int? ParentDepartmentId { get; set; }
    public string Name { get; set; }
}

public class ShopProductLink
{
    public int LinkId { get; set; }
    public int FromProductId { get; set; }
    public int ToProductId { get; set; }
}
