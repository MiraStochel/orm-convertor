public void Query()
{
    var q = ctx.ShopCustomers
        .Select(c => new { CustomerId = c.CustomerId, Label = c.Notes == null ? null : c.Name + " (" + c.Notes + ")" })
        .ToList();
}
