// The NHibernate source of the differential matrix (decision 089): the persistent class,
// whose mapping stands beside it in Product.hbm.xml. Every mapped member is virtual, as
// NHibernate requires of a class it proxies.
namespace Shop;

public class DifferentialProduct
{
    public virtual int ProductId { get; set; }

    public virtual string ProductName { get; set; }

    public virtual string Sku { get; set; }

    public virtual decimal UnitPrice { get; set; }

    public virtual double? Weight { get; set; }

    public virtual bool IsDiscontinued { get; set; }
}
