SELECT * FROM {{schema}}.ShopOrders AS o
WHERE (SELECT COUNT(*) FROM {{schema}}.ShopOrderLines AS ol
       WHERE ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId) >= @minLines
