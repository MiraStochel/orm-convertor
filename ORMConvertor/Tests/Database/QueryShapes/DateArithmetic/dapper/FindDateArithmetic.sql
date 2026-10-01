SELECT o.CompanyId AS CompanyId, o.OrderId AS OrderId,
       DATEADD(day, 30, o.PlacedAt) AS DueAt,
       DATEDIFF(hour, '2025-01-01T00:00:00', o.PlacedAt) AS HoursFromNewYear
FROM {{schema}}.ShopOrders AS o
WHERE DATEDIFF(day, o.PlacedAt, '2025-03-01T00:00:00') > 0
