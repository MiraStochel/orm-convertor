select ol.ProductId as ProductId, avg(cast(ol.Quantity as double)) as AverageQuantity
from ShopOrderLine ol
group by ol.ProductId
