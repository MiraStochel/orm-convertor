SELECT * FROM Sales.OrderLines AS ol WHERE ol.UnitPrice > (SELECT AVG(p.UnitPrice) FROM Sales.Products AS p WHERE p.UnitPrice > 1)
