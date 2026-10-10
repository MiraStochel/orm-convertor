from ShopDepartment d where d.DepartmentId < all (select c.ParentDepartmentId from ShopDepartment c where c.DepartmentId > d.DepartmentId)
