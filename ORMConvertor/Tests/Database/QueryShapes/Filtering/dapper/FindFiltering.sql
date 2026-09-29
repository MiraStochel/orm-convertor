SELECT * FROM Sales.OrderLines AS ol WHERE (ol.Quantity > 5 OR ol.UnitPrice >= 100.5) AND ol.Description IS NOT NULL AND NOT (ol.ProductId = 3)
