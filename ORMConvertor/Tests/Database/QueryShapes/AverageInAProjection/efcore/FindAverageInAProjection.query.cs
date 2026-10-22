public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => ol.ProductId)
        .Select(g => new { ProductId = g.Key, AverageQuantity = (int)g.Average(x => x.Quantity), AveragePrice = g.Average(x => x.UnitPrice) })
        .ToList();
}
