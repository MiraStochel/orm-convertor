from ShopOrderLine ol where ol.ProductId in (select p.ProductId from ShopProduct p where p.UnitPrice > 100)
