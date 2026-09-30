public void Query()
{
    var q = ctx.ShopOrderLines.Select(ol => new { LineNumber = ol.LineNumber, Volume = ol.Quantity > 5 ? "bulk" : "single" })
        .ToList();
}
