select ol.LineNumber as LineNumber, case when ol.Quantity > 10 then 'bulk' else case when ol.Quantity > 5 then 'several' else 'single' end end as Volume from ShopOrderLine ol
