public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => ol.ProductId)
        .Where(g => g.Sum(x => x.Quantity) > 10)
        .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity), Lines = g.Count() })
        .ToList();
}
