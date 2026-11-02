using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace Shop;

public static class FindFilteringQuery
{
    public static List<ShopOrderLine> FindFiltering(IDbConnection connection)
    {
        const string sql = @"
            SELECT *
            FROM {{schema}}.ShopOrderLines AS ol
            WHERE (ol.Quantity > 5 OR ol.UnitPrice >= 100.5) AND ol.Description IS NOT NULL AND NOT (ol.ProductId = 3)";

        return connection.Query<ShopOrderLine>(sql).ToList();
    }
}
