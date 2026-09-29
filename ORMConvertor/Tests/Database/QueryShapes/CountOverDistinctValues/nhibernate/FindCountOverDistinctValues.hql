select ol.ProductId as ProductId, count(distinct ol.OrderId) as Orders from OrderLine ol group by ol.ProductId
