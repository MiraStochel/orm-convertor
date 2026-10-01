public static Query query(EntityManager em, BigDecimal minPrice) {
    return em.createNativeQuery(
            "SELECT p.ProductId AS ProductId, p.ProductName AS ProductName FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > ?1 ORDER BY p.UnitPrice DESC, p.ProductId ASC")
        .setParameter(1, minPrice);
}
