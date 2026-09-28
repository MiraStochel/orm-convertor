// The domain of the query-shape matrices as NHibernate states it: the classes, with the
// virtual members the framework enforces; the mapping stands in Shop.hbm.xml beside them.
using System;

namespace Shop;

public class Customer
{
    public virtual int CustomerId { get; set; }
    public virtual string Name { get; set; }
    public virtual string? Notes { get; set; }
}

public class CustomerOrder
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int CustomerId { get; set; }
    public virtual DateTime PlacedAt { get; set; }
    public virtual bool IsCancelled { get; set; }
}

public class OrderLine
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int LineNumber { get; set; }
    public virtual int ProductId { get; set; }
    public virtual string Description { get; set; }
    public virtual int Quantity { get; set; }
    public virtual decimal UnitPrice { get; set; }
}

public class OrderLineAllocation
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int LineNumber { get; set; }
    public virtual int AllocationId { get; set; }
    public virtual int AllocatedQuantity { get; set; }
    public virtual string? Notes { get; set; }
}

public class Product
{
    public virtual int ProductId { get; set; }
    public virtual string ProductName { get; set; }
    public virtual string Sku { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual bool IsDiscontinued { get; set; }
}
