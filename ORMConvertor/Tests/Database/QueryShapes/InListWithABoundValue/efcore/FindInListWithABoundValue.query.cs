public void Query()
{
    var q = ctx.OrderLines.Where(ol => new[] { 1, 2, extra }.Contains(ol.ProductId))
        .ToList();
}
