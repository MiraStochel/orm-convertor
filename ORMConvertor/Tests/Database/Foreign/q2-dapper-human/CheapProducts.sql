SELECT ProductId, ProductName, Sku, UnitPrice, Weight, IsDiscontinued
FROM {{schema}}.DifferentialProducts
WHERE UnitPrice < 100
