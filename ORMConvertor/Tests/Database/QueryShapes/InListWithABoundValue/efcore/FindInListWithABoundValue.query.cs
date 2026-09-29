public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => new[] { 1, 2, extra }.Contains(ol.ProductId))
        .ToList();
}
