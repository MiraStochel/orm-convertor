public void Query()
{
    var q = ctx.OrderLines.Where(ol => ctx.OrderLineAllocations.Any(a => a.CompanyId == ol.CompanyId && a.OrderId == ol.OrderId && a.LineNumber == ol.LineNumber))
        .ToList();
}
