SELECT c.CustomerId AS CustomerId, c.Name + ' (' + c.Notes + ')' AS Label
FROM {{schema}}.ShopCustomers AS c
