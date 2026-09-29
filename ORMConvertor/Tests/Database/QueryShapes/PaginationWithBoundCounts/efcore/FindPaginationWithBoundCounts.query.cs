public void Query()
{
    var q = ctx.ShopOrderLines.OrderBy(ol => ol.LineNumber).Skip(skip).Take(take)
        .ToList();
}
