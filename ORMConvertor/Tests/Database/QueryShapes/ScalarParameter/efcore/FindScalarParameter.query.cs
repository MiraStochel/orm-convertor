public void Query()
{
    var q = ctx.OrderLines.Where(ol => ol.Quantity >= minQuantity)
        .ToList();
}
