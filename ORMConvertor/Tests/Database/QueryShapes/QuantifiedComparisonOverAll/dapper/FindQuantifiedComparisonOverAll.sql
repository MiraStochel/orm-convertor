SELECT * FROM {{schema}}.ShopDepartments AS d
WHERE d.DepartmentId < ALL (SELECT c.ParentDepartmentId FROM {{schema}}.ShopDepartments AS c
                            WHERE c.DepartmentId > d.DepartmentId)
