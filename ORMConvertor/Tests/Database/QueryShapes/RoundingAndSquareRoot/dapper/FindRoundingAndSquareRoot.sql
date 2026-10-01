SELECT p.ProductId AS ProductId, ROUND(p.UnitPrice, 0) AS RoundedPrice, SQRT(p.UnitPrice) AS PriceRoot
FROM {{schema}}.ShopProducts AS p
WHERE ROUND(p.UnitPrice, 0) > 50
