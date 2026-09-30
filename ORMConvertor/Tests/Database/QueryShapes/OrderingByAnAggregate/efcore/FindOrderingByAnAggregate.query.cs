public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => ol.ProductId)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key)
        .Select(g => new { ProductId = g.Key, Lines = g.Count() })
        .ToList();
}
