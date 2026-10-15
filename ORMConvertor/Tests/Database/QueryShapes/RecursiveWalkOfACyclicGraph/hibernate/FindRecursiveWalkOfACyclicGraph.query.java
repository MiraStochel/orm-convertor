public static Query query(EntityManager em) {
    return em.createQuery("""
            with Walk as (
                select l.ToProductId as ProductId, concat(',1,', cast(l.ToProductId as String), ',') as Path
                from ShopProductLink l
                where l.FromProductId = 1
                union all
                select n.ToProductId as ProductId, concat(w.Path, cast(n.ToProductId as String), ',') as Path
                from Walk w
                join ShopProductLink n on n.FromProductId = w.ProductId
                where w.Path not like concat('%,', cast(n.ToProductId as String), ',%'))
            select distinct w.ProductId as ProductId
            from Walk w
            """)
        .unwrap(org.hibernate.query.Query.class)
        .addQueryHint("MAXRECURSION 10");
}
