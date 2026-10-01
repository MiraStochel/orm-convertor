SELECT COUNT(*) AS Lines, SUM(ol.Quantity) AS Quantity
FROM {{schema}}.ShopOrderLines AS ol
WHERE ol.Quantity > 5
