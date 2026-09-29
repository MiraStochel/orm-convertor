public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ctx.ShopProducts.Where(p => p.UnitPrice > 100).Select(p => p.ProductId).Contains(ol.ProductId))
        .ToList();
}
