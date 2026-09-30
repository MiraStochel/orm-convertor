from ShopCustomer c where coalesce(c.Notes, 'none') = 'none'
