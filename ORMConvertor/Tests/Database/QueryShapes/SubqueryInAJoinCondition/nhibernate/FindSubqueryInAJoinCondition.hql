select p.ProductName as ProductName, ol.Description as Description
from ShopProduct p
left join ShopOrderLine ol with ol.ProductId = p.ProductId and p.ProductId in (select l.ProductId from ShopOrderLine l where l.Quantity > 5)
