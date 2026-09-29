SELECT * FROM {{schema}}.ShopOrderLines AS ol WHERE ol.ProductId IN (1, 2, @extra)
