public void Query()
{
    var q = (from ol in ctx.ShopOrderLines
             from o in ctx.ShopOrders
             where o.CompanyId == ol.CompanyId && o.OrderId == ol.OrderId && o.CustomerId > 0
             select new { Text = ol.Description, o.CustomerId }).ToList();
}
