select year(o.PlacedAt) as PlacedYear, o.CompanyId as CompanyId, count(*) as OrderCount
from ShopOrder o
where o.CustomerId > 0
group by year(o.PlacedAt), o.CompanyId
