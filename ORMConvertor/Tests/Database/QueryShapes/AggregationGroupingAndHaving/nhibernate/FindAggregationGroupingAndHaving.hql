select ol.ProductId as ProductId, sum(ol.Quantity) as Total, count(*) as Lines
from OrderLine ol
group by ol.ProductId
having sum(ol.Quantity) > 10
