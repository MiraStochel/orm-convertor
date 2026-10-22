SELECT p.ProductName AS ProductName, ol.Description AS Description
FROM {{schema}}.ShopProducts AS p
LEFT JOIN {{schema}}.ShopOrderLines AS ol ON ol.ProductId = p.ProductId AND p.ProductId IN (SELECT l.ProductId FROM {{schema}}.ShopOrderLines AS l WHERE l.Quantity > 5)
