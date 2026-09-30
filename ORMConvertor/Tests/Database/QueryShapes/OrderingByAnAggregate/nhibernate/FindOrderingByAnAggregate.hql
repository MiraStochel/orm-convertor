select ol.ProductId as ProductId, count(*) as Lines from ShopOrderLine ol group by ol.ProductId order by count(*) desc, ol.ProductId asc
