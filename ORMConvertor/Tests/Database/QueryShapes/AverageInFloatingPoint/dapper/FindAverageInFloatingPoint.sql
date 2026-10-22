SELECT ol.ProductId AS ProductId, AVG(CAST(ol.Quantity AS FLOAT)) AS AverageQuantity
FROM {{schema}}.ShopOrderLines AS ol
GROUP BY ol.ProductId
