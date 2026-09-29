public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ol.Quantity > 5).Select(ol => new { Text = ol.Description })
        .Union(ctx.ShopProducts.Where(p => p.UnitPrice > 100).Select(p => new { Text = p.ProductName }))
        .ToList();
}
