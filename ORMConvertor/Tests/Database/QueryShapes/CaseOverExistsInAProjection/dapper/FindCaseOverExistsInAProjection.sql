SELECT ol.LineNumber AS LineNumber,
       CASE WHEN EXISTS (SELECT a.AllocationId FROM {{schema}}.ShopOrderLineAllocations AS a
                         WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber)
            THEN 1 ELSE 0 END AS IsAllocated
FROM {{schema}}.ShopOrderLines AS ol
WHERE ol.Quantity > 1
