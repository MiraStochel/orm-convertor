select year(o.PlacedAt) as PlacedYear, o.CompanyId as CompanyId, case when o.CustomerId = 1 then 1 else 0 end as KeyAccount, count(*) as OrderCount
from ShopOrder o
where o.CustomerId > 0
group by year(o.PlacedAt), o.CompanyId, case when o.CustomerId = 1 then 1 else 0 end
