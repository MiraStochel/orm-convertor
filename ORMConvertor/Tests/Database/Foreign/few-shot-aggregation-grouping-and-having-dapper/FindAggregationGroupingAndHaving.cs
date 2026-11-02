public static List<dynamic> FindAggregationGroupingAndHaving(IDbConnection connection)
{
    const string sql = @"
        SELECT ol.ProductId AS ProductId, SUM(ol.Quantity) AS Total, COUNT(*) AS Lines
        FROM {{schema}}.ShopOrderLines AS ol
        GROUP BY ol.ProductId
        HAVING SUM(ol.Quantity) > 10";

    return connection.Query(sql).ToList();
}
