public void Query()
{
    var q = ctx.OrderLines.Where(ol => ids.Contains(ol.ProductId))
        .ToList();
}
