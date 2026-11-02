package Shop;

import jakarta.persistence.EntityManager;
import jakarta.persistence.Query;

public final class FindAggregationGroupingAndHavingQuery {

    public static Query findAggregationGroupingAndHaving(EntityManager em) {
        return em.createQuery(
                "select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(ol) as Lines "
                + "from ShopOrderLine ol "
                + "group by ol.ProductId "
                + "having sum(ol.Quantity) > 10");
    }
}
