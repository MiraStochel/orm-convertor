SELECT * FROM Sales.OrderLines AS ol ORDER BY ol.LineNumber ASC OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY
