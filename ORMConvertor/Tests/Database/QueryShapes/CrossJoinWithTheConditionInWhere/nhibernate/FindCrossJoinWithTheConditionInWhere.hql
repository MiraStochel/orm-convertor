select ol.Description as Text, o.CustomerId as CustomerId
from ShopOrderLine ol, ShopOrder o
where o.CompanyId = ol.CompanyId and o.OrderId = ol.OrderId and o.CustomerId > 0
