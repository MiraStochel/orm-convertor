SELECT ol.ProductId AS ProductId, SUM(ol.Quantity) AS Total, COUNT(*) AS Lines
FROM {{schema}}.ShopOrderLines AS ol
GROUP BY ol.ProductId
HAVING SUM(ol.Quantity) > 10
