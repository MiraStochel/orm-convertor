public void Query()
{
    var q = ctx.ShopOrderLines.OrderBy(ol => ol.ProductId).ThenByDescending(ol => ol.Quantity)
        .ToList();
}
