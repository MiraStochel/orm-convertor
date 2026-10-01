WITH Ranked AS (
    SELECT ol.ProductId AS ProductId, ol.LineNumber AS LineNumber, ol.Quantity AS Quantity,
           ROW_NUMBER() OVER (PARTITION BY ol.ProductId ORDER BY ol.Quantity DESC, ol.LineNumber ASC) AS RowNumber
    FROM {{schema}}.ShopOrderLines AS ol)
SELECT r.ProductId AS ProductId, r.LineNumber AS LineNumber, r.Quantity AS Quantity
FROM Ranked AS r
WHERE r.RowNumber = 1
