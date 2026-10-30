SELECT ol.Description AS Text, o.CustomerId AS CustomerId
FROM {{schema}}.ShopOrderLines AS ol, {{schema}}.ShopOrders AS o
WHERE o.CompanyId = ol.CompanyId AND o.OrderId = ol.OrderId AND o.CustomerId > 0
