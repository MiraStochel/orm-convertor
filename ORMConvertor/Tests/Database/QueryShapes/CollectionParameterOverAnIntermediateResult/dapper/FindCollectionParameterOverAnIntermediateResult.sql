WITH LineCount AS (
    SELECT ol.CompanyId AS CompanyId, ol.OrderId AS OrderId, COUNT(*) AS Lines
    FROM {{schema}}.ShopOrderLines AS ol
    WHERE ol.ProductId IN @ids
    GROUP BY ol.CompanyId, ol.OrderId)
SELECT o.CompanyId AS CompanyId, o.OrderId AS OrderId, lc.Lines AS Lines
FROM {{schema}}.ShopOrders AS o
JOIN LineCount AS lc ON lc.CompanyId = o.CompanyId AND lc.OrderId = o.OrderId
