using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Shop;

public static class FindAggregationGroupingAndHavingQuery
{
    public static IQueryable FindAggregationGroupingAndHaving(DbContext ctx)
    {
        return ctx.Set<ShopOrderLine>()
            .GroupBy(ol => ol.ProductId)
            .Where(g => g.Sum(x => x.Quantity) > 10)
            .Select(g => new { ProductId = g.Key, Total = g.Sum(x => x.Quantity), Lines = g.Count() });
    }
}
