public void Query()
{
    var q = ctx.ShopOrders
        .Where(o => o.CustomerId > 0)
        .GroupBy(o => new { PlacedYear = o.PlacedAt.Year, o.CompanyId })
        .Select(g => new { PlacedYear = g.Key.PlacedYear, CompanyId = g.Key.CompanyId, OrderCount = g.Count() })
        .ToList();
}
