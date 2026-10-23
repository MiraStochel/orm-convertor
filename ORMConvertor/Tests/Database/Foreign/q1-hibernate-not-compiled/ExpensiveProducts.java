public static TypedQuery<DifferentialProduct> expensiveProducts(EntityManager em) {
    return em.createQuery(
            "select p from DifferentialProduct p where p.UnitPrice > 100 order by p.ProductName asc",
            DifferentialProduct.class);
}
