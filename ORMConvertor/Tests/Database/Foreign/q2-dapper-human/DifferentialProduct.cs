namespace Shop;

public class DifferentialProduct
{
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal UnitPrice { get; set; }

    public double? Weight { get; set; }

    public bool IsDiscontinued { get; set; }
}
