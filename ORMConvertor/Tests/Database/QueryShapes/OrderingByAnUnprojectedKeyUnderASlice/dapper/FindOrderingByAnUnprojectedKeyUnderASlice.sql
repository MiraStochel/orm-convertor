SELECT TOP (3) p.ProductName AS ProductName, COUNT(*) AS Lines
FROM {{schema}}.ShopOrderLines AS ol
INNER JOIN {{schema}}.ShopProducts AS p ON p.ProductId = ol.ProductId
GROUP BY p.ProductId, p.ProductName
ORDER BY Lines DESC, p.ProductId ASC
