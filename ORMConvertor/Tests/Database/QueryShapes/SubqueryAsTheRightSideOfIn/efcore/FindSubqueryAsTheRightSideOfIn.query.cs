public void Query()
{
    var q = ctx.OrderLines.Where(ol => ctx.Products.Where(p => p.UnitPrice > 100).Select(p => p.ProductId).Contains(ol.ProductId))
        .ToList();
}
