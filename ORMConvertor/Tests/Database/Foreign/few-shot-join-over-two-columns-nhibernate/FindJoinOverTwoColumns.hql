select ol.Description as Text, o.CustomerId as CustomerId
from ShopOrderLine ol
inner join ShopOrder o with o.CompanyId = ol.CompanyId and o.OrderId = ol.OrderId
where o.CustomerId > 0
