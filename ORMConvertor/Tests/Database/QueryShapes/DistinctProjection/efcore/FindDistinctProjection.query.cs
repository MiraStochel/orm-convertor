public void Query()
{
    var q = ctx.OrderLines.Select(ol => new { ProductId = ol.ProductId }).Distinct()
        .ToList();
}
