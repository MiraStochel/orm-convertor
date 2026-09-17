// An NHibernate source unit of the Java suite (decision 078). NHibernate splits the
// mapping over two artifacts, so the class carries the language types and Shop.hbm.xml
// beside it the database facts.
namespace Shop;

public class Product
{
    public virtual int ProductId { get; set; }

    public virtual string ProductName { get; set; }

    public virtual string Sku { get; set; }

    public virtual decimal UnitPrice { get; set; }

    public virtual bool IsDiscontinued { get; set; }
}
