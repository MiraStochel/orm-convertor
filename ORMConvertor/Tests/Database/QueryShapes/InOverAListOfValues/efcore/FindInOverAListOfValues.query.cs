public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => new[] { 1, 2, 3 }.Contains(ol.ProductId))
        .ToList();
}
