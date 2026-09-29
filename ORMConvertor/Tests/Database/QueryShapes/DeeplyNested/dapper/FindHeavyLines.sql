-- The deliberately bad query in T-SQL, as a Dapper source: eight subqueries four levels
-- deep, three joins of which one runs over two columns and one over three, grouping with
-- a HAVING, ordering and a slice. The two thresholds and the slice are literals here,
-- because a parameter of a Dapper source has no mapping to take its scalar from without
-- a catalog (decision 083); every other source binds them. The table of the ShopOrder
-- class is spelled ShopOrders, which the naming rule of decision 050 singularizes.
SELECT ol.ProductId AS ProductId, SUM(ol.Quantity) AS TotalQuantity, COUNT(*) AS Lines
FROM {{schema}}.ShopOrderLines AS ol
INNER JOIN {{schema}}.ShopOrders AS o ON o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId
INNER JOIN {{schema}}.ShopOrderLineAllocations AS a ON a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber
LEFT JOIN {{schema}}.ShopProducts AS p ON p.ProductId = ol.ProductId
WHERE ol.Quantity >= 2
  AND o.CustomerId IN (
    SELECT DISTINCT c.CustomerId
    FROM {{schema}}.ShopCustomers AS c
    WHERE c.CustomerId IN (
      SELECT o2.CustomerId
      FROM {{schema}}.ShopOrders AS o2
      WHERE o2.OrderId IN (
        SELECT ol2.OrderId
        FROM {{schema}}.ShopOrderLines AS ol2
        WHERE ol2.UnitPrice > (
          SELECT AVG(p2.UnitPrice)
          FROM {{schema}}.ShopProducts AS p2
          WHERE p2.ProductId IN (1, 2, 3)))))
  AND EXISTS (
    SELECT a2.AllocationId
    FROM {{schema}}.ShopOrderLineAllocations AS a2
    WHERE a2.CompanyId = ol.CompanyId AND a2.OrderId = ol.OrderId AND a2.LineNumber = ol.LineNumber
      AND a2.AllocatedQuantity > (
        SELECT MIN(ol3.Quantity)
        FROM {{schema}}.ShopOrderLines AS ol3
        WHERE ol3.ProductId = ol.ProductId))
  AND NOT EXISTS (
    SELECT p3.ProductId
    FROM {{schema}}.ShopProducts AS p3
    WHERE p3.ProductId = ol.ProductId
      AND p3.UnitPrice < (
        SELECT MAX(ol4.UnitPrice)
        FROM {{schema}}.ShopOrderLines AS ol4
        WHERE ol4.CompanyId = ol.CompanyId))
  AND (ol.Description IS NOT NULL OR ol.UnitPrice > (
    SELECT AVG(ol5.UnitPrice)
    FROM {{schema}}.ShopOrderLines AS ol5
    WHERE ol5.Quantity > 0))
GROUP BY ol.ProductId
HAVING SUM(ol.Quantity) > 10
ORDER BY ol.ProductId ASC
OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY
