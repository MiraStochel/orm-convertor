public void Query()
{
    var q = ctx.OrderLines.Where(ol => ol.UnitPrice > ctx.Products.Where(p => p.UnitPrice > 1).Average(p => p.UnitPrice))
        .ToList();
}
