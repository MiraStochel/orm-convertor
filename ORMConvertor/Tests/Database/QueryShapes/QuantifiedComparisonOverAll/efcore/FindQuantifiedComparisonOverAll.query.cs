public void Query()
{
    var q = ctx.ShopDepartments.Where(d => ctx.ShopDepartments.Where(c => c.DepartmentId > d.DepartmentId).All(c => d.DepartmentId < c.ParentDepartmentId))
        .ToList();
}
