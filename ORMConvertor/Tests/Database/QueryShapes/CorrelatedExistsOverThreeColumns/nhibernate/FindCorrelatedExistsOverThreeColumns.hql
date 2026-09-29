from OrderLine ol where exists (select a.AllocationId from OrderLineAllocation a where a.CompanyId = ol.CompanyId and a.OrderId = ol.OrderId and a.LineNumber = ol.LineNumber)
