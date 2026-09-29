SELECT ol.ProductId AS ProductId, COUNT(DISTINCT ol.OrderId) AS Orders
FROM {{schema}}.ShopOrderLines AS ol
GROUP BY ol.ProductId
