public void Query()
{
    var lineCount = ctx.ShopOrderLines
        .GroupBy(ol => new { ol.CompanyId, ol.OrderId })
        .Select(g => new { CompanyId = g.Key.CompanyId, OrderId = g.Key.OrderId, Lines = g.Count() });
    var q = ctx.ShopOrders
        .Join(lineCount, o => new { o.CompanyId, o.OrderId }, lc => new { lc.CompanyId, lc.OrderId }, (o, lc) => new { o, lc })
        .Where(x => x.lc.Lines >= lineCount.Max(m => m.Lines))
        .Select(x => new { CompanyId = x.o.CompanyId, OrderId = x.o.OrderId, Lines = x.lc.Lines })
        .ToList();
}
