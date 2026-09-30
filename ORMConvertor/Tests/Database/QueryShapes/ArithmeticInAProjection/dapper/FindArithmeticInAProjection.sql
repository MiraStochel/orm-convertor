SELECT ol.LineNumber AS LineNumber, ol.Quantity * ol.UnitPrice AS Total FROM {{schema}}.ShopOrderLines AS ol
