SELECT * FROM {{schema}}.ShopOrderLines AS ol WHERE ol.ProductId IN (SELECT p.ProductId FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > 100)
