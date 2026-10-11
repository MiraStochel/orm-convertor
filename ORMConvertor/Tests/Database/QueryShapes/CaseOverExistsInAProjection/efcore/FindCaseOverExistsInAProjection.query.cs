public void Query()
{
    var q = ctx.ShopOrderLines
        .Where(ol => ol.Quantity > 1)
        .Select(ol => new
        {
            LineNumber = ol.LineNumber,
            IsAllocated = ctx.ShopOrderLineAllocations.Any(a => a.CompanyId == ol.CompanyId && a.OrderId == ol.OrderId && a.LineNumber == ol.LineNumber) ? 1 : 0,
        })
        .ToList();
}
