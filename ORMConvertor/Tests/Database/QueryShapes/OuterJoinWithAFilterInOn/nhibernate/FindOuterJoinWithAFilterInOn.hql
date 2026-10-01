select p.ProductName as ProductName, ol.Description as Description
from ShopProduct p
left join ShopOrderLine ol with ol.ProductId = p.ProductId and ol.Quantity > 5
