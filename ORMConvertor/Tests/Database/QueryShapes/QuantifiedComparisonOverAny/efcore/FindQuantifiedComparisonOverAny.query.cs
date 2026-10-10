public void Query()
{
    var q = ctx.ShopDepartments.Where(d => ctx.ShopDepartments.Where(c => c.ParentDepartmentId == d.DepartmentId).Select(c => c.DepartmentId).Any(v => d.ParentDepartmentId < v))
        .ToList();
}
