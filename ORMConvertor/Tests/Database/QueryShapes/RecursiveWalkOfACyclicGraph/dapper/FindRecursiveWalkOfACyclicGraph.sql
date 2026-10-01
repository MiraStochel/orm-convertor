WITH Walk AS (
    SELECT l.ToProductId AS ProductId, 1 AS Steps
    FROM {{schema}}.ShopProductLinks AS l
    WHERE l.FromProductId = 1
    UNION ALL
    SELECT n.ToProductId, w.Steps + 1
    FROM Walk AS w
    JOIN {{schema}}.ShopProductLinks AS n ON n.FromProductId = w.ProductId
    WHERE w.Steps < 3)
SELECT DISTINCT w.ProductId AS ProductId
FROM Walk AS w
OPTION (MAXRECURSION 10)
