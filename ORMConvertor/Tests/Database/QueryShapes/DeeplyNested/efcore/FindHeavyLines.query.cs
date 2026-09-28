// The deliberately bad query as a LINQ chain over a DbContext, valid C# over a real
// context. It keeps to what the shared LINQ parser reads after a join: keys and filters of
// the root, whole rows through the joins, grouping and aggregates over the root's columns;
// the nesting therefore sits in the filter, as correlated subqueries over the root.
public void Query()
{
    var q = ctx.OrderLines
        .Where(ol => ol.Quantity >= minQuantity
            && ctx.Orders
                .Where(o2 => ctx.Customers
                    .Where(c => c.Notes != null
                        && ctx.OrderLines
                            .Where(ol2 => ol2.UnitPrice > ctx.Products
                                .Where(p2 => new[] { 1, 2, 3 }.Contains(p2.ProductId))
                                .Average(p2 => p2.UnitPrice))
                            .Select(ol2 => ol2.OrderId)
                            .Contains(o2.OrderId))
                    .Select(c => c.CustomerId)
                    .Contains(o2.CustomerId))
                .Select(o2 => o2.OrderId)
                .Distinct()
                .Contains(ol.OrderId)
            && ctx.OrderLineAllocations.Any(a2 => a2.CompanyId == ol.CompanyId
                && a2.OrderId == ol.OrderId
                && a2.LineNumber == ol.LineNumber
                && a2.AllocatedQuantity > ctx.OrderLines
                    .Where(ol3 => ol3.ProductId == ol.ProductId)
                    .Min(ol3 => ol3.Quantity))
            && !ctx.Products.Any(p3 => p3.ProductId == ol.ProductId
                && p3.UnitPrice < ctx.OrderLines
                    .Where(ol4 => ol4.CompanyId == ol.CompanyId)
                    .Max(ol4 => ol4.UnitPrice))
            && (ol.Description != null || ol.UnitPrice > ctx.OrderLines
                .Where(ol5 => ol5.Quantity > 0)
                .Average(ol5 => ol5.UnitPrice)))
        .Join(ctx.Orders,
            ol => new { ol.CompanyId, ol.OrderId },
            o => new { o.CompanyId, o.OrderId },
            (ol, o) => new { ol, o })
        .Join(ctx.OrderLineAllocations,
            x => new { x.ol.CompanyId, x.ol.OrderId, x.ol.LineNumber },
            a => new { a.CompanyId, a.OrderId, a.LineNumber },
            (x, a) => new { x.ol, x.o, a })
        .LeftJoin(ctx.Products,
            x => x.ol.ProductId,
            p => p.ProductId,
            (x, p) => new { x.ol, x.o, x.a, p })
        .GroupBy(x => x.ol.ProductId)
        .Where(g => g.Sum(x => x.ol.Quantity) > minTotal)
        .OrderBy(g => g.Key)
        .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(x => x.ol.Quantity), Lines = g.Count() })
        .Skip(skip)
        .Take(take)
        .ToList();
}
