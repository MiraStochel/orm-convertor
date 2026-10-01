public void Query()
{
    var q = ctx.ShopOrderLines
        .GroupBy(ol => new { ol.CompanyId, ol.OrderId })
        .Select(g => new { CompanyId = g.Key.CompanyId, OrderId = g.Key.OrderId, Lines = g.Count() })
        .Where(lc => lc.CompanyId > 1)
        .GroupBy(lc => lc.Lines)
        .Select(g => new { Lines = g.Key, Orders = g.Count() })
        .ToList();
}
