// A condition over both rows that is no equality has no place among the keys of a LINQ
// join: the lines of every product are filtered by it for each product, and DefaultIfEmpty()
// keeps a product none of whose lines passes - the correlated form of a left join.
public void Query()
{
    var q = ctx.ShopProducts
        .SelectMany(p => ctx.ShopOrderLines
                .Where(ol => ol.ProductId == p.ProductId && ol.UnitPrice < p.UnitPrice)
                .DefaultIfEmpty(),
            (p, ol) => new { p, ol })
        .Select(x => new { x.p.ProductName, x.ol.Description })
        .ToList();
}
