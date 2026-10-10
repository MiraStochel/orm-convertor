from ShopOrderLine ol where ol.ProductId in (select ol2.ProductId from ShopOrderLine ol2 inner join ShopOrder o with o.CompanyId = ol2.CompanyId and o.OrderId = ol2.OrderId where o.CustomerId > 1)
