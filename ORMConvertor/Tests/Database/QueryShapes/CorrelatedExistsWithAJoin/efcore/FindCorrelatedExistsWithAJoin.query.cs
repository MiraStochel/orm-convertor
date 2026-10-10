public void Query()
{
    var q = ctx.ShopOrderLines
        .Where(ol => ctx.ShopOrderLineAllocations
            .Join(ctx.ShopOrders,
                a => new { a.CompanyId, a.OrderId },
                o => new { o.CompanyId, o.OrderId },
                (a, o) => new { a, o })
            .Any(x => x.a.CompanyId == ol.CompanyId && x.a.OrderId == ol.OrderId && x.a.LineNumber == ol.LineNumber && x.o.CustomerId > 1))
        .ToList();
}
