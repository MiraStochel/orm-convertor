// The Dapper source of the differential matrix (decision 089): property names and language
// types and nothing else, because Dapper has no way to state anything else. Table name,
// columns, key and facets all come out of the catalog (decision 015), which is what makes
// this row the F6 case of the matrix as well.
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
