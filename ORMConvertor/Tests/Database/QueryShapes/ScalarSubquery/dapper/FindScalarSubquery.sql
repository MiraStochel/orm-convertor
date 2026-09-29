SELECT * FROM {{schema}}.ShopOrderLines AS ol WHERE ol.UnitPrice > (SELECT AVG(p.UnitPrice) FROM {{schema}}.ShopProducts AS p WHERE p.UnitPrice > 1)
