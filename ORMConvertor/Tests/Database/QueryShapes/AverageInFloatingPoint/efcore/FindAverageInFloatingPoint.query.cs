public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => ol.ProductId)
        .Select(g => new { ProductId = g.Key, AverageQuantity = g.Average(x => x.Quantity) })
        .ToList();
}
