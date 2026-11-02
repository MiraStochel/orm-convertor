using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Shop;

public static class FindProjectionQuery
{
    public static IQueryable FindProjection(DbContext ctx)
    {
        return ctx.Set<ShopOrderLine>()
            .Select(ol => new { Text = ol.Description, Qty = ol.Quantity });
    }
}
