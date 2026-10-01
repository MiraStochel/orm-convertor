SELECT ol.ProductId AS ProductId, STRING_AGG(ol.Description, '; ') WITHIN GROUP (ORDER BY ol.LineNumber ASC) AS Descriptions
FROM {{schema}}.ShopOrderLines AS ol
WHERE ol.Quantity > 1
GROUP BY ol.ProductId
