public void Query()
{
    var q = ctx.ShopProducts.Where(p => ctx.ShopOrderLines.Where(ol => ol.Quantity > 5).Select(ol => ol.ProductId).Union(ctx.ShopProductLinks.Where(l => l.FromProductId == 4).Select(l => l.ToProductId)).Contains(p.ProductId))
        .Select(p => new { p.ProductName, p.UnitPrice })
        .ToList();
}
