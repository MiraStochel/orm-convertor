SELECT lc.Lines AS Lines, COUNT(*) AS Orders
FROM (SELECT ol.CompanyId AS CompanyId, ol.OrderId AS OrderId, COUNT(*) AS Lines
      FROM {{schema}}.ShopOrderLines AS ol
      GROUP BY ol.CompanyId, ol.OrderId) AS lc
WHERE lc.CompanyId > 1
GROUP BY lc.Lines
