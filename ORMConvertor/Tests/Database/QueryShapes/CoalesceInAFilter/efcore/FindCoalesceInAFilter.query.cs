public void Query()
{
    var q = ctx.ShopCustomers.Where(c => (c.Notes ?? "none") == "none")
        .ToList();
}
