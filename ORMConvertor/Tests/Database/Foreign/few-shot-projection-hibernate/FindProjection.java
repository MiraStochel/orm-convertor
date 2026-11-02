public static Query findProjection(EntityManager em) {
    return em.createQuery(
            "select ol.Description as Text, ol.Quantity as Qty from ShopOrderLine ol");
}
