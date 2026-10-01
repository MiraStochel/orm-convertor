public void Query()
{
    var q = ctx.ShopProducts
        .Where(p => Math.Round(p.UnitPrice, 0) > 50)
        .Select(p => new { ProductId = p.ProductId, RoundedPrice = Math.Round(p.UnitPrice, 0), PriceRoot = Math.Sqrt((double)p.UnitPrice) })
        .ToList();
}
