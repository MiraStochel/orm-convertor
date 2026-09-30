SELECT * FROM {{schema}}.ShopCustomers AS c WHERE COALESCE(c.Notes, 'none') = 'none'
