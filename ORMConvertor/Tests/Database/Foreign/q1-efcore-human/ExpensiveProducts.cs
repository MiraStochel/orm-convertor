public static IQueryable<DifferentialProduct> ExpensiveProducts(DbContext ctx)
{
    return ctx.Set<DifferentialProduct>()
        .Where(product => product.UnitPrice > 100m)
        .OrderBy(product => product.ProductName);
}
