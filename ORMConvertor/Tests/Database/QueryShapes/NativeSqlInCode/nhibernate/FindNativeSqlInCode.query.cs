public IQuery Query(ISession session, decimal minPrice)
{
    return session.CreateSQLQuery(
            "SELECT p.ProductId AS ProductId, p.ProductName AS ProductName FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > :minPrice ORDER BY p.UnitPrice DESC, p.ProductId ASC")
        .SetParameter("minPrice", minPrice);
}
