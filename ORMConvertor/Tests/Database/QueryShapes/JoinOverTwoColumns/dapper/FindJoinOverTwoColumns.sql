SELECT ol.Description AS Text, o.CustomerId AS CustomerId
FROM Sales.OrderLines AS ol
INNER JOIN Sales.CustomerOrders AS o ON o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId
WHERE o.CustomerId > 0
