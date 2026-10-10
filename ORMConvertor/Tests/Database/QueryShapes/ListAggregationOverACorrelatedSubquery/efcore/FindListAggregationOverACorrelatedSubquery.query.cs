public void Query()
{
    var q = ctx.ShopProducts
        .Where(p => p.UnitPrice > 20)
        .Select(p => new { ProductId = p.ProductId, ProductName = p.ProductName, Descriptions = ctx.ShopOrderLines.Where(ol => ol.ProductId == p.ProductId && ol.Quantity > 2).GroupBy(ol => 1).Select(g => string.Join("; ", g.OrderBy(ol => ol.LineNumber).Select(ol => ol.Description))).FirstOrDefault() })
        .ToList();
}
