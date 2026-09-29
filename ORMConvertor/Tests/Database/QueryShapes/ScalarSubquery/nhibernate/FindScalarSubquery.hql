from ShopOrderLine ol where ol.UnitPrice > (select avg(p.UnitPrice) from ShopProduct p where p.UnitPrice > 1)
