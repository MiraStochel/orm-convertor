// The domain of the query-shape matrices as Dapper states it: plain classes, no attribute,
// no key - every mapping fact is the catalog's to supply (F6). The class of the Orders
// table is called CustomerOrder because `order` is a keyword of HQL and JPQL, so the SQL
// of this source names the table CustomerOrders and the naming rule of decision 050
// finds the class. Read by both test suites from this one file.
using System;

namespace Shop;

public class Customer
{
    public int CustomerId { get; set; }
    public string Name { get; set; }
    public string? Notes { get; set; }
}

public class CustomerOrder
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int CustomerId { get; set; }
    public DateTime PlacedAt { get; set; }
    public bool IsCancelled { get; set; }
}

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

public class OrderLineAllocation
{
    public int CompanyId { get; set; }
    public int OrderId { get; set; }
    public int LineNumber { get; set; }
    public int AllocationId { get; set; }
    public int AllocatedQuantity { get; set; }
    public string? Notes { get; set; }
}

public class Product
{
    public int ProductId { get; set; }
    public string ProductName { get; set; }
    public string Sku { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsDiscontinued { get; set; }
}
