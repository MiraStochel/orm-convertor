public void Query()
{
    var q = ctx.ShopOrderLines
        .Join(ctx.ShopProducts, ol => ol.ProductId, p => p.ProductId, (ol, p) => new { ol, p })
        .GroupBy(x => new { x.p.ProductId, x.p.ProductName })
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key.ProductId)
        .Select(g => new { ProductName = g.Key.ProductName, Lines = g.Count() })
        .Take(3)
        .ToList();
}
