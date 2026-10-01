from ShopOrder o where (select count(*) from ShopOrderLine ol where ol.CompanyId = o.CompanyId and ol.OrderId = o.OrderId) >= :minLines
