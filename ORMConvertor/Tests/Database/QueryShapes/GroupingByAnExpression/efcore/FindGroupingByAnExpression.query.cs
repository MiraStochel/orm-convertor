public void Query()
{
    var q = ctx.ShopOrders
        .Where(o => o.CustomerId > 0)
        .GroupBy(o => new { PlacedYear = o.PlacedAt.Year, o.CompanyId, KeyAccount = o.CustomerId == 1 ? 1 : 0 })
        .Select(g => new { PlacedYear = g.Key.PlacedYear, CompanyId = g.Key.CompanyId, KeyAccount = g.Key.KeyAccount, OrderCount = g.Count() })
        .ToList();
}
