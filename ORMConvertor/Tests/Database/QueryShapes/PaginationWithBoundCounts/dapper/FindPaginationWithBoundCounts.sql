SELECT * FROM {{schema}}.ShopOrderLines AS ol ORDER BY ol.LineNumber ASC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
