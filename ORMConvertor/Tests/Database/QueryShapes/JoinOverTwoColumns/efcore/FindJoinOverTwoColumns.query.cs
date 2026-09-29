public void Query()
{
    var q = ctx.ShopOrderLines
        .Join(ctx.ShopOrders,
            ol => new { ol.CompanyId, ol.OrderId },
            o => new { o.CompanyId, o.OrderId },
            (ol, o) => new { ol, o })
        .Where(x => x.o.CustomerId > 0)
        .Select(x => new { Text = x.ol.Description, x.o.CustomerId })
        .ToList();
}
