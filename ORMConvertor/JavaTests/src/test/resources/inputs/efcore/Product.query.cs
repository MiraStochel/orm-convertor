// The same query as the JPQL unit, in the language EF Core reads: a LINQ chain over the
// DbSet. The extension says CSharpQuery - the frontend has a picker to tell a query from
// an entity, a file name has not, so the longer extension decides here.
public void Query()
{
    var q = ctx.Products
        .Where(p => p.UnitPrice > 100)
        .OrderBy(p => p.ProductName)
        .ToList();
}
