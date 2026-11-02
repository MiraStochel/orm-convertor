using NHibernate;

namespace Shop;

public static class FindAggregationGroupingAndHavingQuery
{
    public static IQuery FindAggregationGroupingAndHaving(ISession session)
    {
        return session.CreateQuery(
            "select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(*) as Lines "
            + "from ShopOrderLine ol "
            + "group by ol.ProductId "
            + "having sum(ol.Quantity) > 10");
    }
}
