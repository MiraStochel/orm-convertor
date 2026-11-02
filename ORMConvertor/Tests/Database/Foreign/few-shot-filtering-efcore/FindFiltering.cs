public static IQueryable<ShopOrderLine> FindFiltering(DbContext ctx)
{
    return ctx.Set<ShopOrderLine>()
        .Where(ol => (ol.Quantity > 5 || ol.UnitPrice >= 100.5m) && ol.Description != null && !(ol.ProductId == 3));
}
