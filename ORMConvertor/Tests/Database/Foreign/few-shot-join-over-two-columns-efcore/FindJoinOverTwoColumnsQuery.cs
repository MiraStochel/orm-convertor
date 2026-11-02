using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Shop;

public static class FindJoinOverTwoColumnsQuery
{
    public static IQueryable FindJoinOverTwoColumns(DbContext ctx)
    {
        return ctx.Set<ShopOrderLine>()
            .Join(ctx.Set<ShopOrder>(),
                ol => new { ol.CompanyId, ol.OrderId },
                o => new { o.CompanyId, o.OrderId },
                (ol, o) => new { ol, o })
            .Where(x => x.o.CustomerId > 0)
            .Select(x => new { Text = x.ol.Description, CustomerId = x.o.CustomerId });
    }
}
