public void Query()
{
    var q = ctx.ShopOrders.Where(o => ctx.ShopOrderLines.Where(ol => ol.CompanyId == o.CompanyId && ol.OrderId == o.OrderId).Count() >= minLines)
        .ToList();
}
