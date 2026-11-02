using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;

namespace Shop;

public static class FindProjectionQuery
{
    public static List<dynamic> FindProjection(IDbConnection connection)
    {
        const string sql = @"
            SELECT ol.Description AS Text, ol.Quantity AS Qty
            FROM {{schema}}.ShopOrderLines AS ol";

        return connection.Query(sql).ToList();
    }
}
