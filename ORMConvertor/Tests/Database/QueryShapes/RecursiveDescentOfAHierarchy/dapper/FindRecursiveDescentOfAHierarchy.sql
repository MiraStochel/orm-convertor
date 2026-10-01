WITH DepartmentTree AS (
    SELECT d.DepartmentId AS DepartmentId, d.Name AS Name, 0 AS Depth
    FROM {{schema}}.ShopDepartments AS d
    WHERE d.ParentDepartmentId IS NULL
    UNION ALL
    SELECT c.DepartmentId, c.Name, t.Depth + 1
    FROM {{schema}}.ShopDepartments AS c
    JOIN DepartmentTree AS t ON c.ParentDepartmentId = t.DepartmentId)
SELECT t.DepartmentId AS DepartmentId, t.Name AS Name, t.Depth AS Depth
FROM DepartmentTree AS t
WHERE t.Depth >= 2
