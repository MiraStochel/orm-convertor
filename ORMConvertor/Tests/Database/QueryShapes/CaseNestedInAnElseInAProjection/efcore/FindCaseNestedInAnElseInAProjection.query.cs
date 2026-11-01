public void Query()
{
    var q = ctx.ShopOrderLines.Select(ol => new { LineNumber = ol.LineNumber, Volume = ol.Quantity > 10 ? "bulk" : (ol.Quantity > 5 ? "several" : "single") })
        .ToList();
}
