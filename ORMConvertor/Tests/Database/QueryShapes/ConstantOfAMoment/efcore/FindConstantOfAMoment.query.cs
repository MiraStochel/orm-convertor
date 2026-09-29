public void Query()
{
    var q = ctx.ShopOrders.Where(o => o.PlacedAt > new DateTime(2025, 1, 1))
        .ToList();
}
