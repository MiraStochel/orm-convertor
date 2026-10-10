SELECT * FROM {{schema}}.ShopDepartments AS d
WHERE d.ParentDepartmentId < SOME (SELECT c.DepartmentId FROM {{schema}}.ShopDepartments AS c
                                   WHERE c.ParentDepartmentId = d.DepartmentId)
