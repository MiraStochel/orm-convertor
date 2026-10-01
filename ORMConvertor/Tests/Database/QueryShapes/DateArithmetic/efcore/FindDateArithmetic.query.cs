public void Query()
{
    var q = ctx.ShopOrders
        .Where(o => EF.Functions.DateDiffDay(o.PlacedAt, new DateTime(2025, 3, 1)) > 0)
        .Select(o => new
        {
            CompanyId = o.CompanyId,
            OrderId = o.OrderId,
            DueAt = o.PlacedAt.AddDays(30),
            HoursFromNewYear = EF.Functions.DateDiffHour(new DateTime(2025, 1, 1), o.PlacedAt),
        })
        .ToList();
}
