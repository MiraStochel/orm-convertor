using System;

namespace Shop;

public class ShopCustomer
{
    public virtual int CustomerId { get; set; }
    public virtual string Name { get; set; }
    public virtual string? Notes { get; set; }
}

[Serializable]
public class ShopOrder
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int CustomerId { get; set; }
    public virtual DateTime PlacedAt { get; set; }
    public virtual bool IsCancelled { get; set; }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not ShopOrder other)
        {
            return false;
        }

        return Equals(CompanyId, other.CompanyId)
            && Equals(OrderId, other.OrderId);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CompanyId, OrderId);
    }
}

[Serializable]
public class ShopOrderLine
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int LineNumber { get; set; }
    public virtual int ProductId { get; set; }
    public virtual string Description { get; set; }
    public virtual int Quantity { get; set; }
    public virtual decimal UnitPrice { get; set; }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not ShopOrderLine other)
        {
            return false;
        }

        return Equals(CompanyId, other.CompanyId)
            && Equals(OrderId, other.OrderId)
            && Equals(LineNumber, other.LineNumber);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CompanyId, OrderId, LineNumber);
    }
}

[Serializable]
public class ShopOrderLineAllocation
{
    public virtual int CompanyId { get; set; }
    public virtual int OrderId { get; set; }
    public virtual int LineNumber { get; set; }
    public virtual int AllocationId { get; set; }
    public virtual int AllocatedQuantity { get; set; }
    public virtual string? Notes { get; set; }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj is not ShopOrderLineAllocation other)
        {
            return false;
        }

        return Equals(CompanyId, other.CompanyId)
            && Equals(OrderId, other.OrderId)
            && Equals(LineNumber, other.LineNumber)
            && Equals(AllocationId, other.AllocationId);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(CompanyId, OrderId, LineNumber, AllocationId);
    }
}

public class ShopProduct
{
    public virtual int ProductId { get; set; }
    public virtual string ProductName { get; set; }
    public virtual string Sku { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual bool IsDiscontinued { get; set; }
}

public class ShopDepartment
{
    public virtual int DepartmentId { get; set; }
    public virtual int? ParentDepartmentId { get; set; }
    public virtual string Name { get; set; }
}

public class ShopProductLink
{
    public virtual int LinkId { get; set; }
    public virtual int FromProductId { get; set; }
    public virtual int ToProductId { get; set; }
}
