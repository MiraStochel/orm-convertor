SELECT * FROM {{schema}}.ShopOrderLines AS ol
WHERE EXISTS (SELECT a.AllocationId FROM {{schema}}.ShopOrderLineAllocations AS a
              WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber)
