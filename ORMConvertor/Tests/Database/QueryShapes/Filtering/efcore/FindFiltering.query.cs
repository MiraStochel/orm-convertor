public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => (ol.Quantity > 5 || ol.UnitPrice >= 100.5m) && ol.Description != null && !(ol.ProductId == 3))
        .ToList();
}
