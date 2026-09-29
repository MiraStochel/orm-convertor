public void Query()
{
    var q = ctx.ShopOrderLines.Select(ol => new { Text = ol.Description, Qty = ol.Quantity })
        .ToList();
}
