select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(*) as Lines
from ShopOrderLine ol
group by ol.ProductId
having sum(ol.Quantity) > 10
