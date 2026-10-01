public IQueryable<ProductRow> Query(ShopContext ctx, decimal minPrice)
{
    return ctx.Database.SqlQuery<ProductRow>(
        $"SELECT p.ProductId AS ProductId, p.ProductName AS ProductName FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > {minPrice} ORDER BY p.UnitPrice DESC, p.ProductId ASC");
}
