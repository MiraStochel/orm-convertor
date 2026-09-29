SELECT ol.Description AS Text, o.CustomerId AS CustomerId
FROM {{schema}}.ShopOrderLines AS ol
INNER JOIN {{schema}}.ShopOrders AS o ON o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId
WHERE o.CustomerId > 0
