SELECT ol.ProductId AS ProductId, AVG(ol.Quantity) AS AverageQuantity, AVG(ol.UnitPrice) AS AveragePrice
FROM {{schema}}.ShopOrderLines AS ol
GROUP BY ol.ProductId
