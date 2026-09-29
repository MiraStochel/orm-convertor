SELECT ol.Description AS Text FROM Sales.OrderLines AS ol WHERE ol.Quantity > 5
UNION
SELECT p.ProductName AS Text FROM Sales.Products AS p WHERE p.UnitPrice > 100
