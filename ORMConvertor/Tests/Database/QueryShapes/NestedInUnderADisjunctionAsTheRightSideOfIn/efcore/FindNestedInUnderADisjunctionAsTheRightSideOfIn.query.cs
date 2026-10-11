public void Query()
{
    var q = ctx.ShopOrderLines
        .Where(ol => ol.Quantity > 1
            && ctx.ShopProductLinks
                .Where(l2 => l2.FromProductId == 3
                    || ctx.ShopProductLinks.Where(l1 => l1.FromProductId == 3).Select(l1 => l1.ToProductId).Contains(l2.FromProductId))
                .Select(l2 => l2.ToProductId)
                .Contains(ol.ProductId))
        .ToList();
}
