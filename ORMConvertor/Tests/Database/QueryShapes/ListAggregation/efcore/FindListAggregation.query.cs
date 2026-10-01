public void Query()
{
    var q = ctx.ShopOrderLines
        .Where(ol => ol.Quantity > 1)
        .GroupBy(ol => ol.ProductId)
        .Select(g => new { ProductId = g.Key, Descriptions = string.Join("; ", g.OrderBy(x => x.LineNumber).Select(x => x.Description)) })
        .ToList();
}
