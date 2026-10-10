SELECT p.ProductId AS ProductId, p.ProductName AS ProductName, (SELECT STRING_AGG(ol.Description, '; ') WITHIN GROUP (ORDER BY ol.LineNumber ASC) FROM {{schema}}.ShopOrderLines AS ol WHERE ol.ProductId = p.ProductId AND ol.Quantity > 2) AS Descriptions
FROM {{schema}}.ShopProducts AS p
WHERE p.UnitPrice > 20
