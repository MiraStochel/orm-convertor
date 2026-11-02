public static TypedQuery<ShopOrderLine> findFiltering(EntityManager em) {
    return em.createQuery(
            "select ol from ShopOrderLine ol where (ol.Quantity > 5 or ol.UnitPrice >= 100.5) and ol.Description is not null and not (ol.ProductId = 3)",
            ShopOrderLine.class);
}
