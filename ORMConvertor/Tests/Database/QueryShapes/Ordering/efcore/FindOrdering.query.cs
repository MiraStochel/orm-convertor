public void Query()
{
    var q = ctx.OrderLines.OrderBy(ol => ol.ProductId).ThenByDescending(ol => ol.Quantity)
        .ToList();
}
