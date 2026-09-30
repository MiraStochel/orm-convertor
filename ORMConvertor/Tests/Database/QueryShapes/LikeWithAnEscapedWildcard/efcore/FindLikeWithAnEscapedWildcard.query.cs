public void Query()
{
    var q = ctx.ShopProducts.Where(p => p.ProductName.StartsWith("W_"))
        .ToList();
}
