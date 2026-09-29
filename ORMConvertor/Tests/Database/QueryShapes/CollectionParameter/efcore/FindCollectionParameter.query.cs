public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ids.Contains(ol.ProductId))
        .ToList();
}
