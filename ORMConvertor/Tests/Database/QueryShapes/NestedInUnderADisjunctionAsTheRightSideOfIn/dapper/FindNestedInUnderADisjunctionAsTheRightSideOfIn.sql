SELECT * FROM {{schema}}.ShopOrderLines AS ol
WHERE ol.Quantity > 1
  AND ol.ProductId IN (SELECT l2.ToProductId FROM {{schema}}.ShopProductLinks AS l2
                       WHERE l2.FromProductId = 3
                          OR l2.FromProductId IN (SELECT l1.ToProductId FROM {{schema}}.ShopProductLinks AS l1 WHERE l1.FromProductId = 3))
