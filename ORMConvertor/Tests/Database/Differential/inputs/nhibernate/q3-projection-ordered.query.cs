// A projection row of the matrix: the result is not the entity but a pair of fields, in a
// stated order. That order is what the swapped-fields mutation of decision 089 attacks.
public void Query()
{
    var q = session.Query<DifferentialProduct>()
        .Where(p => p.UnitPrice > 100)
        .OrderBy(p => p.ProductName)
        .Select(p => new { p.ProductName, p.UnitPrice })
        .ToList();
}
