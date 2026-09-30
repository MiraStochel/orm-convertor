public void Query()
{
    var q = ctx.ShopOrderLines.Select(ol => new { LineNumber = ol.LineNumber, Total = ol.Quantity * ol.UnitPrice })
        .ToList();
}
