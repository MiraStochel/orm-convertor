public static IQueryable FindProjection(DbContext ctx)
{
    return ctx.Set<ShopOrderLine>()
        .Select(ol => new { Text = ol.Description, Qty = ol.Quantity });
}
