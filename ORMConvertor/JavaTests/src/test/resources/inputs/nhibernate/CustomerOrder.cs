namespace Shop;

using System;

public class CustomerOrder
{
    public virtual int CompanyId { get; set; }

    public virtual int OrderId { get; set; }

    public virtual int CustomerId { get; set; }

    public virtual DateOnly OrderDate { get; set; }

    public virtual bool IsCancelled { get; set; }
}
