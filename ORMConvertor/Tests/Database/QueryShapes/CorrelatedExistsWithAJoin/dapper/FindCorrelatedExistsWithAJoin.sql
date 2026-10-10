SELECT * FROM {{schema}}.ShopOrderLines AS ol
WHERE EXISTS (SELECT a.AllocationId FROM {{schema}}.ShopOrderLineAllocations AS a
              INNER JOIN {{schema}}.ShopOrders AS o ON o.CompanyId = a.CompanyId AND o.OrderId = a.OrderId
              WHERE a.CompanyId = ol.CompanyId AND a.OrderId = ol.OrderId AND a.LineNumber = ol.LineNumber AND o.CustomerId > 1)
