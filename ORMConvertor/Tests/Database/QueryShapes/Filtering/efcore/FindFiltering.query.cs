public void Query()
{
    var q = ctx.OrderLines.Where(ol => (ol.Quantity > 5 || ol.UnitPrice >= 100.5m) && ol.Description != null && !(ol.ProductId == 3))
        .ToList();
}
