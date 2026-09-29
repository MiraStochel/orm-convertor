public void Query()
{
    var q = ctx.ShopOrderLines.Where(ol => ol.Quantity >= minQuantity)
        .ToList();
}
