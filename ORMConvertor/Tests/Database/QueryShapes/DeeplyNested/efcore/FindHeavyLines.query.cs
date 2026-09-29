// The deliberately bad query as a LINQ chain over a DbContext, valid C# over a real
// context, and the same query the SQL-shaped rows state: the joins first, then the filter
// over the joined row - its members are how LINQ reaches a joined table (x.o.CustomerId),
// the correlated subqueries included -, grouping and aggregates over the root's columns.
public void Query()
{
    var q = ctx.OrderLines
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
        .Where(x => x.ol.Quantity >= minQuantity
            && ctx.Customers
                .Where(c => ctx.Orders
                    .Where(o2 => ctx.OrderLines
                        .Where(ol2 => ol2.UnitPrice > ctx.Products
                            .Where(p2 => new[] { 1, 2, 3 }.Contains(p2.ProductId))
                            .Average(p2 => p2.UnitPrice))
                        .Select(ol2 => ol2.OrderId)
                        .Contains(o2.OrderId))
                    .Select(o2 => o2.CustomerId)
                    .Contains(c.CustomerId))
                .Select(c => c.CustomerId)
                .Distinct()
                .Contains(x.o.CustomerId)
            && ctx.OrderLineAllocations.Any(a2 => a2.CompanyId == x.ol.CompanyId
                && a2.OrderId == x.ol.OrderId
                && a2.LineNumber == x.ol.LineNumber
                && a2.AllocatedQuantity > ctx.OrderLines
                    .Where(ol3 => ol3.ProductId == x.ol.ProductId)
                    .Min(ol3 => ol3.Quantity))
            && !ctx.Products.Any(p3 => p3.ProductId == x.ol.ProductId
                && p3.UnitPrice < ctx.OrderLines
                    .Where(ol4 => ol4.CompanyId == x.ol.CompanyId)
                    .Max(ol4 => ol4.UnitPrice))
            && (x.ol.Description != null || x.ol.UnitPrice > ctx.OrderLines
                .Where(ol5 => ol5.Quantity > 0)
                .Average(ol5 => ol5.UnitPrice)))
        .GroupBy(x => x.ol.ProductId)
        .Where(g => g.Sum(x => x.ol.Quantity) > minTotal)
        .OrderBy(g => g.Key)
        .Select(g => new { ProductId = g.Key, TotalQuantity = g.Sum(x => x.ol.Quantity), Lines = g.Count() })
        .Skip(skip)
        .Take(take)
        .ToList();
}
