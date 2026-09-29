public void Query()
{
    var q = ctx.OrderLines.Select(ol => new { Text = ol.Description, Qty = ol.Quantity })
        .ToList();
}
