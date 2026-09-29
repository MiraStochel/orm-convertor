SELECT ol.Description AS Text FROM {{schema}}.ShopOrderLines AS ol WHERE ol.Quantity > 5
UNION
SELECT p.ProductName AS Text FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > 100
