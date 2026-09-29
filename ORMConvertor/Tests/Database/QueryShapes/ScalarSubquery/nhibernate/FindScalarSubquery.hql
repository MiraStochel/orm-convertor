from OrderLine ol where ol.UnitPrice > (select avg(p.UnitPrice) from Product p where p.UnitPrice > 1)
