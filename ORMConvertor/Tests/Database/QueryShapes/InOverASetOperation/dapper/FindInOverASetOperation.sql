SELECT p.ProductName, p.UnitPrice FROM {{schema}}.ShopProducts AS p
WHERE p.ProductId IN (SELECT ol.ProductId FROM {{schema}}.ShopOrderLines AS ol WHERE ol.Quantity > 5
                      UNION
                      SELECT l.ToProductId FROM {{schema}}.ShopProductLinks AS l WHERE l.FromProductId = 4)
