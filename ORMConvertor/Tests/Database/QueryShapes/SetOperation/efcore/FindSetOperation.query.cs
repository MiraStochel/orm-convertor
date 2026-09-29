public void Query()
{
    var q = ctx.OrderLines.Where(ol => ol.Quantity > 5).Select(ol => new { Text = ol.Description })
        .Union(ctx.Products.Where(p => p.UnitPrice > 100).Select(p => new { Text = p.ProductName }))
        .ToList();
}
