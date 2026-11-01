SELECT ol.LineNumber AS LineNumber, CASE WHEN ol.Quantity > 10 THEN 'bulk' ELSE CASE WHEN ol.Quantity > 5 THEN 'several' ELSE 'single' END END AS Volume FROM {{schema}}.ShopOrderLines AS ol
