public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ctx.ShopOrderLineAllocations.Any(a => a.CompanyId == ol.CompanyId && a.OrderId == ol.OrderId && a.LineNumber == ol.LineNumber))
        .ToList();
}
