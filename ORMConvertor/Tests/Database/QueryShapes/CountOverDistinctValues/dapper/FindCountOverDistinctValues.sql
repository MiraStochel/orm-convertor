SELECT ol.ProductId AS ProductId, COUNT(DISTINCT ol.OrderId) AS Orders
FROM Sales.OrderLines AS ol
GROUP BY ol.ProductId
