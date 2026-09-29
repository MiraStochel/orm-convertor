public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => ol.ProductId)
        .Select(g => new { ProductId = g.Key, Orders = g.Select(x => x.OrderId).Distinct().Count() })
        .ToList();
}
