// A subquery in the condition of a join names the row of the chain, so the join is the
// correlated SelectMany (decision 113) with Contains in its predicate, and DefaultIfEmpty()
// keeps a product that no line of a product ordered in bulk matches - the left join it is.
public void Query()
{
    var q = ctx.ShopProducts
        .SelectMany(p => ctx.ShopOrderLines
                .Where(ol => ol.ProductId == p.ProductId && ctx.ShopOrderLines.Where(l => l.Quantity > 5).Select(l => l.ProductId).Contains(p.ProductId))
                .DefaultIfEmpty(),
            (p, ol) => new { p, ol })
        .Select(x => new { x.p.ProductName, x.ol.Description })
        .ToList();
}
