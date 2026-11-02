using NHibernate;

namespace Shop;

public static class FindFilteringQuery
{
    public static IQuery FindFiltering(ISession session)
    {
        return session.CreateQuery(
            "from ShopOrderLine ol where (ol.Quantity > 5 or ol.UnitPrice >= 100.5) and ol.Description is not null and not (ol.ProductId = 3)");
    }
}
