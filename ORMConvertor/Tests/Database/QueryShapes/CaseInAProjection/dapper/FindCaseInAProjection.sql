SELECT ol.LineNumber AS LineNumber, CASE WHEN ol.Quantity > 5 THEN 'bulk' ELSE 'single' END AS Volume FROM {{schema}}.ShopOrderLines AS ol
