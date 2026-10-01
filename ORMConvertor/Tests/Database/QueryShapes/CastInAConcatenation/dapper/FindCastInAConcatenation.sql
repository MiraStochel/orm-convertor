SELECT p.ProductId AS ProductId, '#' + CAST(p.ProductId AS NVARCHAR(MAX)) + ' ' + p.ProductName AS Label
FROM {{schema}}.ShopProducts AS p
WHERE CAST(p.UnitPrice AS INT) > 100
