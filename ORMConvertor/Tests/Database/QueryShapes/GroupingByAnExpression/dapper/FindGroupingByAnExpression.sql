SELECT YEAR(o.PlacedAt) AS PlacedYear, o.CompanyId AS CompanyId, CASE WHEN o.CustomerId = 1 THEN 1 ELSE 0 END AS KeyAccount, COUNT(*) AS OrderCount
FROM {{schema}}.ShopOrders AS o
WHERE o.CustomerId > 0
GROUP BY YEAR(o.PlacedAt), o.CompanyId, CASE WHEN o.CustomerId = 1 THEN 1 ELSE 0 END
