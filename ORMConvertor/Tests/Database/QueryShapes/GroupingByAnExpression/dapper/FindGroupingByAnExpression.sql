SELECT YEAR(o.PlacedAt) AS PlacedYear, o.CompanyId AS CompanyId, COUNT(*) AS OrderCount
FROM {{schema}}.ShopOrders AS o
WHERE o.CustomerId > 0
GROUP BY YEAR(o.PlacedAt), o.CompanyId
