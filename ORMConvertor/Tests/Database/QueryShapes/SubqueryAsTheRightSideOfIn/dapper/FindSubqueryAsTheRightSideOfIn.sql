SELECT * FROM Sales.OrderLines AS ol WHERE ol.ProductId IN (SELECT p.ProductId FROM Sales.Products AS p WHERE p.UnitPrice > 100)
