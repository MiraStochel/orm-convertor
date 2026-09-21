SELECT *
FROM {{schema}}.DifferentialProducts AS p
WHERE p.UnitPrice > 100
ORDER BY p.ProductName ASC
