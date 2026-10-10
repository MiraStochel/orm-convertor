from ShopDepartment d where d.ParentDepartmentId < some (select c.DepartmentId from ShopDepartment c where c.ParentDepartmentId = d.DepartmentId)
