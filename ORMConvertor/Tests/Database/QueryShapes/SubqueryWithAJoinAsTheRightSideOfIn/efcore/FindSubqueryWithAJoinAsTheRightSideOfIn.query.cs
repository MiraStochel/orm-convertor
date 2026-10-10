public void Query()
{
    var q = ctx.ShopOrderLines
        .Where(ol => ctx.ShopOrderLines
            .Join(ctx.ShopOrders,
                ol2 => new { ol2.CompanyId, ol2.OrderId },
                o => new { o.CompanyId, o.OrderId },
                (ol2, o) => new { ol2, o })
            .Where(x => x.o.CustomerId > 1)
            .Select(x => x.ol2.ProductId)
            .Contains(ol.ProductId))
        .ToList();
}
