SELECT * FROM Sales.OrderLines AS ol
WHERE EXISTS (SELECT a.AllocationId FROM Sales.OrderLineAllocations AS a
              WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber)
