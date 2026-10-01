// The filter of the join is the joined set's own: a left join keeps a product whose lines
// it rejects, exactly as the ON of the SQL-shaped rows does.
public void Query()
{
    var q = ctx.ShopProducts
        .LeftJoin(ctx.ShopOrderLines.Where(ol => ol.Quantity > 5),
            p => p.ProductId,
            ol => ol.ProductId,
            (p, ol) => new { p, ol })
        .Select(x => new { x.p.ProductName, x.ol.Description })
        .ToList();
}
