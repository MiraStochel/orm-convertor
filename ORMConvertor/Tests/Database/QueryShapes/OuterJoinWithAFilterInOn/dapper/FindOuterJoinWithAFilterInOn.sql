SELECT p.ProductName AS ProductName, ol.Description AS Description
FROM {{schema}}.ShopProducts AS p
LEFT JOIN {{schema}}.ShopOrderLines AS ol ON ol.ProductId = p.ProductId AND ol.Quantity > 5
