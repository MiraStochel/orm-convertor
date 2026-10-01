select p.ProductId as ProductId, concat('#', cast(p.ProductId as string), ' ', p.ProductName) as Label
from ShopProduct p
where cast(p.UnitPrice as int) > 100
