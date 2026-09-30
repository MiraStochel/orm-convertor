SELECT ol.ProductId AS ProductId, COUNT(*) AS Lines
FROM {{schema}}.ShopOrderLines AS ol
GROUP BY ol.ProductId
ORDER BY COUNT(*) DESC, ol.ProductId ASC
