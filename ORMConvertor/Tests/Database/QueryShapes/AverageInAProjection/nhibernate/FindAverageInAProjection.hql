select ol.ProductId as ProductId, cast(avg(ol.Quantity) as int) as AverageQuantity, cast(avg(ol.UnitPrice) as decimal) as AveragePrice
from ShopOrderLine ol
group by ol.ProductId
