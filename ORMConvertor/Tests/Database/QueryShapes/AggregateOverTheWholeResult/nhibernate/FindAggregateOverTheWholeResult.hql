select count(*) as Lines, sum(ol.Quantity) as Quantity
from ShopOrderLine ol
where ol.Quantity > 5
