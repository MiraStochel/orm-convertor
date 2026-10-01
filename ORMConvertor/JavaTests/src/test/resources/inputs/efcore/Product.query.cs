// The same query as the JPQL unit, in the language EF Core reads: a LINQ chain over the
// DbSet. The unit is C# like any other .cs file - a unit declares its language only, and the
// source framework finds the query in it - so the .query infix just tells a reader of the tree
// what the file holds.
public void Query()
{
    var q = ctx.Products
        .Where(p => p.UnitPrice > 100)
        .OrderBy(p => p.ProductName)
        .ToList();
}
