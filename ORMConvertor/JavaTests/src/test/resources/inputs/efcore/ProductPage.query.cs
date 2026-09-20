// A page of the products, which is how paging is written where it is written at all: the
// row count comes from the caller and not from the text (decision 085). The suite runs the
// generated artifact against the database, so what is proved here is that the placeholder
// the target wrote really binds - a slice of one row out of the two the test wrote.
public void Query()
{
    var q = ctx.Products
        .OrderBy(p => p.ProductName)
        .Take(take)
        .ToList();
}
