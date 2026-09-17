// The same query in the language NHibernate reads: a LINQ chain over the session.
public void Query()
{
    var q = session.Query<Product>()
        .Where(p => p.UnitPrice > 100)
        .OrderBy(p => p.ProductName)
        .ToList();
}
