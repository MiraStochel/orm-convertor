select ol.ProductId as ProductId, sum(ol.Quantity) as TotalQuantity, count(*) as Lines
from ShopOrderLine ol
inner join ShopOrder o with o.CompanyId = ol.CompanyId and o.OrderId = ol.OrderId
inner join ShopOrderLineAllocation a with a.CompanyId = ol.CompanyId and a.OrderId = ol.OrderId and a.LineNumber = ol.LineNumber
left join ShopProduct p with p.ProductId = ol.ProductId
where ol.Quantity >= :minQuantity
  and o.CustomerId in (
    select distinct c.CustomerId
    from ShopCustomer c
    where c.CustomerId in (
      select o2.CustomerId
      from ShopOrder o2
      where o2.OrderId in (
        select ol2.OrderId
        from ShopOrderLine ol2
        where ol2.UnitPrice > (
          select avg(p2.UnitPrice)
          from ShopProduct p2
          where p2.ProductId in (1, 2, 3)))))
  and exists (
    select a2.AllocationId
    from ShopOrderLineAllocation a2
    where a2.CompanyId = ol.CompanyId and a2.OrderId = ol.OrderId and a2.LineNumber = ol.LineNumber
      and a2.AllocatedQuantity > (
        select min(ol3.Quantity)
        from ShopOrderLine ol3
        where ol3.ProductId = ol.ProductId))
  and not exists (
    select p3.ProductId
    from ShopProduct p3
    where p3.ProductId = ol.ProductId
      and p3.UnitPrice < (
        select max(ol4.UnitPrice)
        from ShopOrderLine ol4
        where ol4.CompanyId = ol.CompanyId))
  and (ol.Description is not null or ol.UnitPrice > (
    select avg(ol5.UnitPrice)
    from ShopOrderLine ol5
    where ol5.Quantity > 0))
group by ol.ProductId
having sum(ol.Quantity) > :minTotal
order by ol.ProductId asc
