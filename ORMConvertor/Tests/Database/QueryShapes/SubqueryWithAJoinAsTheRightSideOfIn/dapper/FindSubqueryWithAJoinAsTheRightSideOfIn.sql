SELECT * FROM {{schema}}.ShopOrderLines AS ol
WHERE ol.ProductId IN (SELECT ol2.ProductId FROM {{schema}}.ShopOrderLines AS ol2
                       INNER JOIN {{schema}}.ShopOrders AS o ON o.CompanyId = ol2.CompanyId AND o.OrderId = ol2.OrderId
                       WHERE o.CustomerId > 1)
