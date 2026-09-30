public void Query()
{
    var q = ctx.ShopProducts.Where(p => p.ProductName.Length == 6)
        .ToList();
}
