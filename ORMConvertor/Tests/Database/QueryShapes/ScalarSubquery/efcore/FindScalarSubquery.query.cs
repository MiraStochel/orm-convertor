public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ol.UnitPrice > ctx.ShopProducts.Where(p => p.UnitPrice > 1).Average(p => p.UnitPrice))
        .ToList();
}
