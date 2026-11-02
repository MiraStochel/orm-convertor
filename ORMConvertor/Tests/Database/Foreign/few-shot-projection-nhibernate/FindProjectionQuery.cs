using NHibernate;

namespace Shop;

public static class FindProjectionQuery
{
    public static IQuery FindProjection(ISession session)
    {
        return session.CreateQuery(
            "select ol.Description as Text, ol.Quantity as Qty from ShopOrderLine ol");
    }
}
