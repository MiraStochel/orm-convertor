namespace Shop;

public class DifferentialProduct
{
    public virtual int ProductId { get; set; }

    public virtual string ProductName { get; set; } = string.Empty;

    public virtual string Sku { get; set; } = string.Empty;

    public virtual decimal UnitPrice { get; set; }

    public virtual double? Weight { get; set; }

    public virtual bool IsDiscontinued { get; set; }
}
