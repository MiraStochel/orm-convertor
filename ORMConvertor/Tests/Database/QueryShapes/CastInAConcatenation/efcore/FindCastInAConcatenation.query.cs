public void Query()
{
    var q = ctx.ShopProducts
        .Where(p => (int)p.UnitPrice > 100)
        .Select(p => new { ProductId = p.ProductId, Label = "#" + p.ProductId.ToString() + " " + p.ProductName })
        .ToList();
}
