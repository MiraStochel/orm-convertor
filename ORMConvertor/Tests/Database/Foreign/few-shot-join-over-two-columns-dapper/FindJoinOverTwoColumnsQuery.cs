using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace Shop;

public static class FindJoinOverTwoColumnsQuery
{
    public static List<dynamic> FindJoinOverTwoColumns(IDbConnection connection)
    {
        const string sql = @"
            SELECT ol.Description AS Text, o.CustomerId AS CustomerId
            FROM {{schema}}.ShopOrderLines AS ol
            INNER JOIN {{schema}}.ShopOrders AS o ON o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId
            WHERE o.CustomerId > 0";

        return connection.Query(sql).ToList();
    }
}
