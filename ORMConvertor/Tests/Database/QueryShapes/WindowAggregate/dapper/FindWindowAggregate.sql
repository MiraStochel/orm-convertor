WITH Averaged AS (
    SELECT ol.ProductId AS ProductId, ol.LineNumber AS LineNumber, ol.UnitPrice AS UnitPrice,
           AVG(ol.UnitPrice) OVER (PARTITION BY ol.ProductId) AS AveragePrice
    FROM {{schema}}.ShopOrderLines AS ol)
SELECT a.ProductId AS ProductId, a.LineNumber AS LineNumber, a.UnitPrice AS UnitPrice, a.AveragePrice AS AveragePrice
FROM Averaged AS a
WHERE a.UnitPrice > a.AveragePrice
