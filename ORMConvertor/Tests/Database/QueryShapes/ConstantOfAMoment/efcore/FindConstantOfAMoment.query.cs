public void Query()
{
    var q = ctx.Orders.Where(o => o.PlacedAt > new DateTime(2025, 1, 1))
        .ToList();
}
