// The unordered row of the matrix: no ordering instruction reaches the intermediate
// representation, so the comparison is of a set and both sides sort by the rendered line
// before comparing (decision 089).
public void Query()
{
    var q = ctx.DifferentialProducts
        .Where(p => p.UnitPrice < 100)
        .ToList();
}
