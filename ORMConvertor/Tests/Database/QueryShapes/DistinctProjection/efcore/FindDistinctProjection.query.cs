public void Query()
{
    var q = ctx.ShopOrderLines.Select(ol => new { ProductId = ol.ProductId }).Distinct()
        .ToList();
}
