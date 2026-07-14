using Common;
using PetaPoco;
using PetaPoco.Providers;
using PetaPocoEntities;

namespace PetaPocoFeatures;

[Collection("PetaPoco")]
public class FeatureTests
{
    private readonly IDatabase db = DatabaseConfiguration.Build()
         .UsingConnectionString(DatabaseConfig.MSSQLConnectionString)
         .UsingProvider<SqlServerDatabaseProvider>()
         .UsingDefaultMapper<ConventionMapper>(m =>
         {
             m.InflectTableName = (inflector, s) => inflector.Pluralise(s);
             m.InflectColumnName = (inflector, s) => s;
         })
         .Create();

    [Fact]
    public void A1_EntityIdenticalToTable()
    {
        var order = db.Single<PurchaseOrder>(
            "SELECT * FROM WideWorldImporters.Purchasing.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId",
            new { PurchaseOrderId = 25 }
        );

        Assert.Multiple(() =>
        {
            Assert.Equal(25, order.PurchaseOrderID);
            Assert.Equal(12, order.SupplierID);
            Assert.Equal(new DateTime(2013, 1, 5), order.OrderDate);
            Assert.Equal(7, order.DeliveryMethodID);
            Assert.Equal(2, order.ContactPersonID);
            Assert.Equal(new DateTime(2013, 1, 25), order.ExpectedDeliveryDate);
            Assert.Equal("237408032", order.SupplierReference);
            Assert.True(order.IsOrderFinalized);
            Assert.Null(order.Comments);
            Assert.Null(order.InternalComments);
            Assert.Equal(14, order.LastEditedBy);
            Assert.Equal(new DateTime(2013, 1, 7, 7, 0, 0), order.LastEditedWhen);
        });
    }

    [Fact]

    public void A2_LimitedEntity()
    {
        var contactInfo = db.Single<SupplierContactInfo>(
            "WHERE SupplierID = @SupplierID",
            new { SupplierID = 10 }
        );

        Assert.Multiple(() =>
        {
            Assert.Equal(10, contactInfo.SupplierID);
            Assert.Equal("Northwind Electric Cars", contactInfo.SupplierName);
            Assert.Equal("(201) 555-0105", contactInfo.PhoneNumber);
            Assert.Equal("(201) 555-0104", contactInfo.FaxNumber);
            Assert.Equal("http://www.northwindelectriccars.com", contactInfo.WebsiteURL);
            Assert.Equal(new DateTime(2013, 1, 1, 0, 5, 0), contactInfo.ValidFrom);
            Assert.Equal(DateTime.MaxValue, contactInfo.ValidTo);
        });
    }

    [Fact]
    public void A3_MultipleEntitiesFromOneResult()
    {
        var sql = Sql.Builder
            .Select(
                "SupplierId", "SupplierName", "PhoneNumber", "FaxNumber", "WebsiteURL", "ValidFrom", "ValidTo",
                "SupplierId", "BankAccountName", "BankAccountBranch", "BankAccountCode", "BankAccountNumber", "BankInternationalCode")
            .From("WideWorldImporters.Purchasing.Suppliers")
            .Where("SupplierID = @0", 10);

        var result = db.Fetch<SupplierContactInfo, SupplierBankAccount, (SupplierContactInfo, SupplierBankAccount)>(
            (ci, ca) => (ci, ca),
            sql
        );

        var (contactInfo, bankAccount) = result.Single();

        Assert.Multiple(() =>
        {
            Assert.Equal(10, contactInfo.SupplierID);
            Assert.Equal("Northwind Electric Cars", contactInfo.SupplierName);
            Assert.Equal("(201) 555-0105", contactInfo.PhoneNumber);
            Assert.Equal("(201) 555-0104", contactInfo.FaxNumber);
            Assert.Equal("http://www.northwindelectriccars.com", contactInfo.WebsiteURL);
            Assert.Equal(new DateTime(2013, 1, 1, 0, 5, 0), contactInfo.ValidFrom);
            Assert.Equal(DateTime.MaxValue, contactInfo.ValidTo);

            Assert.Equal(10, bankAccount.SupplierID);
            Assert.Equal("Northwind Electric Cars", bankAccount.BankAccountName);
            Assert.Equal("Woodgrove Bank Crandon Lakes", bankAccount.BankAccountBranch);
            Assert.Equal("325447", bankAccount.BankAccountCode);
            Assert.Equal("3258786987", bankAccount.BankAccountNumber);
            Assert.Equal("36214", bankAccount.BankInternationalCode);
        });
    }

    [Fact]
    public void A4_StoredProcedureToEntity()
    {
        var orders = db.FetchProc<PurchaseOrderUpdate>(
            "WideWorldImporters.Integration.GetOrderUpdates",
            new { LastCutoff = new DateTime(2014, 1, 1), NewCutoff = new DateTime(2015, 1, 1) }
        );

        Assert.Multiple(() =>
        {
            Assert.Equal(66741, orders.Count);
            Assert.True(orders.All(o => o.Quantity > 0));
            Assert.True(orders.All(o => o.TaxRate > 0));
            Assert.True(orders.All(o => o.UnitPrice > 0));
        });

        var firstOrder = orders.First();
        Assert.Multiple(() =>
        {
            Assert.Equal(1219, firstOrder.OrderID);
            Assert.Equal("\"The Gu\" red shirt XML tag t-shirt (White) XS", firstOrder.Description);
            Assert.Equal(96, firstOrder.Quantity);
            Assert.Equal(18.00m, firstOrder.UnitPrice);
            Assert.Equal(15m, firstOrder.TaxRate);
            Assert.Equal(1987.2m, firstOrder.TotalIncludingTax);
        });
    }

    [Fact]
    public void B1_SelectionOverIndexedColumn()
    {
        int orderId = 26866;

        var orderLines = db.Fetch<OrderLine>(
            Sql.Builder.Where("OrderID = @0", orderId)
        );

        Assert.Equal(5, orderLines.Count);
        Assert.True(orderLines.All(ol => ol.OrderID == orderId));

        var expectedFirstOrderLine = new OrderLine
        {
            OrderLineID = 85261,
            OrderID = orderId,
            StockItemID = 141,
            Description = "Furry animal socks (Pink) XL",
            PackageTypeID = 10,
            Quantity = 48,
            UnitPrice = 5m,
            TaxRate = 15m,
            PickedQuantity = 48,
            PickingCompletedWhen = new DateTime(2014, 5, 14, 11, 0, 0),
            LastEditedBy = 19,
            LastEditedWhen = new DateTime(2014, 5, 14, 11, 0, 0)
        };

        var actualOrderLine = orderLines.First(ol => ol.OrderLineID == 85261);

        Assert.Multiple(() =>
        {
            Assert.Equal(expectedFirstOrderLine.OrderLineID, actualOrderLine.OrderLineID);
            Assert.Equal(expectedFirstOrderLine.OrderID, actualOrderLine.OrderID);
            Assert.Equal(expectedFirstOrderLine.StockItemID, actualOrderLine.StockItemID);
            Assert.Equal(expectedFirstOrderLine.Description, actualOrderLine.Description);
            Assert.Equal(expectedFirstOrderLine.PackageTypeID, actualOrderLine.PackageTypeID);
            Assert.Equal(expectedFirstOrderLine.Quantity, actualOrderLine.Quantity);
            Assert.Equal(expectedFirstOrderLine.UnitPrice, actualOrderLine.UnitPrice);
            Assert.Equal(expectedFirstOrderLine.TaxRate, actualOrderLine.TaxRate);
            Assert.Equal(expectedFirstOrderLine.PickedQuantity, actualOrderLine.PickedQuantity);
            Assert.Equal(expectedFirstOrderLine.PickingCompletedWhen, actualOrderLine.PickingCompletedWhen);
            Assert.Equal(expectedFirstOrderLine.LastEditedBy, actualOrderLine.LastEditedBy);
            Assert.Equal(expectedFirstOrderLine.LastEditedWhen, actualOrderLine.LastEditedWhen);
        });
    }

    [Fact]
    public void B2_SelectionOverNonIndexedColumn()
    {
        decimal unitPrice = 25m;

        var orderLines = db.Fetch<OrderLine>(
            Sql.Builder.Where("UnitPrice = @0", unitPrice)
        );

        Assert.Equal(13553, orderLines.Count);
        Assert.True(orderLines.All(ol => ol.UnitPrice == unitPrice));
    }

    [Fact]
    public void B3_RangeQuery()
    {
        var from = new DateTime(2014, 12, 20);
        var to = new DateTime(2014, 12, 31);

        var orderLines = db.Fetch<OrderLine>(
            Sql.Builder.Where("PickingCompletedWhen BETWEEN @0 AND @1", from, to)
        );

        Assert.Equal(1883, orderLines.Count);
        Assert.True(orderLines.All(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to));
    }

    [Fact]
    public void B4_InQuery()
    {
        var orderIds = new[] { 1, 10, 100, 1000, 10000 };

        var orderLines = db.Fetch<OrderLine>(
            Sql.Builder.Where("OrderID IN (@0)", orderIds)
        );

        Assert.Equal(15, orderLines.Count);
        Assert.True(orderLines.All(ol => orderIds.Contains(ol.OrderID)));
    }

    [Fact]
    public void B5_TextSearch()
    {
        string text = "C++";

        var orderLines = db.Fetch<OrderLine>(
            Sql.Builder.Where($"Description LIKE @0", $"%{text}%")
        );

        Assert.Equal(2098, orderLines.Count);
        Assert.True(orderLines.All(ol => ol.Description.Contains(text)));
    }

    [Fact]
    public void B6_PagingQuery()
    {
        int skip = 1000;
        int take = 50;

        var orderLines = db.Fetch<OrderLine>(
            "ORDER BY OrderLineID OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY",
            new { Skip = skip, Take = take }
        );

        Assert.Equal(50, orderLines.Count);
        Assert.Equal(Enumerable.Range(skip + 1, take), orderLines.Select(ol => ol.OrderLineID));
    }

    [Fact]
    public void C1_AggregationCount()
    {
        var sql = Sql.Builder
            .Select("TaxRate", "COUNT(TaxRate) as Count")
            .From("Sales.OrderLines")
            .GroupBy("TaxRate")
            .OrderBy("Count DESC");

        var taxRates = db
            .Fetch<dynamic>(sql)
            .ToDictionary(x => (decimal)x.TaxRate, x => (int)x.Count);

        Assert.Equal(2, taxRates.Count);
        Assert.Equal(230376, taxRates[15m]);
        Assert.Equal(1036, taxRates[10m]);
    }

    [Fact]
    public void C2_AggregationMax()
    {
        var sql = Sql.Builder
            .Select("MAX(UnitPrice)")
            .From("Sales.OrderLines");

        var maxUnitPrice = db.Single<decimal>(sql);

        Assert.Equal(1899m, maxUnitPrice);
    }

    [Fact]
    public void C3_AggregationSum()
    {
        var sql = Sql.Builder
            .Select("SUM(Quantity * UnitPrice)")
            .From("Sales.OrderLines");

        var totalSales = db.Single<decimal>(sql);

        Assert.Equal(177634276.4m, totalSales);
    }

    [Fact]
    public void D1_OneToManyRelationship()
    {
        var sql = Sql.Builder
            .Select("*")
            .From("Sales.Orders o")
            .LeftJoin("Sales.OrderLines ol")
            .On("ol.OrderID = o.OrderID")
            .Where("o.OrderID = @0", 530);

        // Like in Dapper matching the two entities has to be done manually
        var order = db.Fetch<Order, OrderLine, Order>(
            (order, orderLine) =>
            {
                order.OrderLines.Add(orderLine);
                return order;
            },
            sql
        )
        .GroupBy(o => o.OrderID)
        .Select(g =>
        {
            var groupedOrder = g.First();
            groupedOrder.OrderLines = g.Select(o => o.OrderLines.Single()).ToList();
            return groupedOrder;
        })
        .Single();

        Assert.NotNull(order);

        var firstOrderLine = order.OrderLines.First();
        Assert.Multiple(() =>
        {
            Assert.Equal(1478, firstOrderLine.OrderLineID);
            Assert.Equal(530, firstOrderLine.OrderID);
            Assert.Equal(129, firstOrderLine.StockItemID);
            Assert.Equal("Plush shark slippers (Gray) XL", firstOrderLine.Description);
            Assert.Equal(7, firstOrderLine.PackageTypeID);
            Assert.Equal(7, firstOrderLine.Quantity);
            Assert.Equal(32.00m, firstOrderLine.UnitPrice);
            Assert.Equal(15.000m, firstOrderLine.TaxRate);
            Assert.Equal(7, firstOrderLine.PickedQuantity);
            Assert.Equal(new DateTime(2013, 1, 10, 11, 0, 0), firstOrderLine.PickingCompletedWhen);
            Assert.Equal(13, firstOrderLine.LastEditedBy);
            Assert.Equal(new DateTime(2013, 1, 10, 11, 0, 0), firstOrderLine.LastEditedWhen);

            Assert.Equal(530, order.OrderID);
            Assert.Equal(115, order.CustomerID);
            Assert.Equal(3, order.SalespersonPersonID);
            Assert.Equal(13, order.PickedByPersonID);
            Assert.Equal(1229, order.ContactPersonID);
            Assert.Null(order.BackorderOrderID);
            Assert.Equal(new DateTime(2013, 1, 10), order.OrderDate);
            Assert.Equal(new DateTime(2013, 1, 11), order.ExpectedDeliveryDate);
            Assert.Equal("19446", order.CustomerPurchaseOrderNumber);
            Assert.True(order.IsUndersupplyBackordered);
            Assert.Null(order.Comments);
            Assert.Null(order.DeliveryInstructions);
            Assert.Null(order.InternalComments);
            Assert.Equal(new DateTime(2013, 1, 10, 11, 0, 0), order.PickingCompletedWhen);
            Assert.Equal(13, order.LastEditedBy);
            Assert.Equal(new DateTime(2013, 1, 10, 11, 0, 0), order.LastEditedWhen);
        });
    }

    [Fact]
    public void D2_ManyToManyRelationship()
    {
        var sqlItems = Sql.Builder
            .Select("si.StockItemID, si.StockItemName, si.SupplierID, si.Brand, si.Size, sg.StockGroupID, sg.StockGroupName")
            .From("Warehouse.StockItems si")
            .LeftJoin("Warehouse.StockItemStockGroups sisg")
            .On("si.StockItemID = sisg.StockItemID")
            .LeftJoin("Warehouse.StockGroups sg")
            .On("sisg.StockGroupID = sg.StockGroupID")
            .OrderBy("si.StockItemID");

        var stockItemsById = new Dictionary<int, StockItem>();

        db.Fetch<StockItem, StockGroup, StockItem>(
            (stockItem, stockGroup) =>
            {
                if (stockItemsById.TryGetValue(stockItem.StockItemID, out var existing))
                {
                    existing.StockGroups.Add(stockGroup);
                    return existing;
                }

                stockItem.StockGroups.Add(stockGroup);
                stockItemsById.Add(stockItem.StockItemID, stockItem);
                return stockItem;
            },
            sqlItems
        );

        var stockItems = stockItemsById.Values.ToList();

        var sqlGroups = Sql.Builder
            .Select("sg.StockGroupID, sg.StockGroupName, si.StockItemID, si.StockItemName, si.SupplierID, si.Brand, si.Size")
            .From("Warehouse.StockGroups sg")
            .LeftJoin("Warehouse.StockItemStockGroups sisg")
            .On("sg.StockGroupID = sisg.StockGroupID")
            .LeftJoin("Warehouse.StockItems si")
            .On("sisg.StockItemID = si.StockItemID")
            .OrderBy("sg.StockGroupID");

        var stockGroupsById = new Dictionary<int, StockGroup>();

        db.Fetch<StockGroup, StockItem, StockGroup>(
            (stockGroup, stockItem) =>
            {
                if (stockGroupsById.TryGetValue(stockGroup.StockGroupID, out var existing))
                {
                    existing.StockItems.Add(stockItem);
                    return existing;
                }

                stockGroup.StockItems.Add(stockItem);
                stockGroupsById.Add(stockGroup.StockGroupID, stockGroup);
                return stockGroup;
            },
            sqlGroups
        );

        var stockGroups = stockGroupsById.Values.ToList();

        Assert.Multiple(() =>
        {
            Assert.Equal(227, stockItems.Count);
            Assert.Equal(10, stockGroups.Count);
        });

        Assert.Multiple(() =>
        {
            Assert.Equal(1, stockItems[0].StockItemID);
            Assert.Equal("USB missile launcher (Green)", stockItems[0].StockItemName);
            Assert.Equal(12, stockItems[0].SupplierID);

            Assert.Equal(3, stockItems[0].StockGroups.Count);
            var groupNames = stockItems[0].StockGroups.Select(sg => sg.StockGroupName).Order().ToList();
            Assert.Equal(["Computing Novelties", "Novelty Items", "USB Novelties"], groupNames);
        });

        Assert.Multiple(() =>
        {
            Assert.Equal(1, stockGroups[0].StockGroupID);
            Assert.Equal("Novelty Items", stockGroups[0].StockGroupName);
            Assert.Equal(91, stockGroups[0].StockItems.Count);
        });

        var group1ItemIds = stockItems
            .Where(si => si.StockGroups.Any(sg => sg.StockGroupID == 1))
            .Select(si => si.StockItemID)
            .ToList();

        Assert.Equal(group1ItemIds, stockGroups[0].StockItems.Select(sisg => sisg.StockItemID).Order().ToList());
    }

    [Fact]
    public void D3_OptionalRelationship()
    {
        var sql = Sql.Builder
            .Select(
                "c.CustomerID", "c.CustomerName", "c.AccountOpenedDate", "c.CreditLimit",
                "ct.CustomerTransactionID", "ct.CustomerID", "ct.TransactionDate", "ct.TransactionAmount")
            .From("Sales.Customers c")
            .LeftJoin("Sales.CustomerTransactions ct")
            .On("c.CustomerID = ct.CustomerID")
            .OrderBy("c.CustomerID");

        var customers = new Dictionary<int, Customer>();

        db.Fetch<Customer, CustomerTransaction, Customer>(
            (customer, transaction) =>
            {
                if (!customers.TryGetValue(customer.CustomerID, out var existing))
                {
                    existing = customer;
                    customers.Add(customer.CustomerID, existing);
                }

                if (transaction.CustomerTransactionID != default)
                {
                    existing.Transactions.Add(transaction);
                }

                return existing;
            },
            sql
        );

        var result = customers.Values.ToList();

        Assert.Equal(663, result.Count);
        Assert.Equal(400, result.Where(c => c.Transactions.Count == 0).Count());
    }

    [Fact]
    public void E1_ColumnSorting()
    {
        var sql = Sql.Builder
            .Select("TOP (1000) *")
            .From("Purchasing.PurchaseOrders")
            .OrderBy("ExpectedDeliveryDate ASC");

        var orders = db.Fetch<PurchaseOrder>(sql);

        Assert.Equal(1000, orders.Count);
        Assert.Equal(new DateTime(2013, 1, 15), orders.First().ExpectedDeliveryDate);
        Assert.Equal(new DateTime(2014, 9, 17), orders.Last().ExpectedDeliveryDate);
        Assert.True(orders.SequenceEqual(orders.OrderBy(o => o.ExpectedDeliveryDate)));
    }

    [Fact]
    public void E2_Distinct()
    {
        var sql = Sql.Builder
            .Select("DISTINCT SupplierReference")
            .From("Purchasing.PurchaseOrders");

        var supplierReferences = db.Fetch<string>(sql);

        Assert.Equal(7, supplierReferences.Count);
        string[] expected = ["AA20384", "BC0280982", "ML0300202", "293092", "08803922", "237408032", "B2084020"];
        Assert.Equal(expected, supplierReferences);
    }

    [Fact]
    public void F1_JSONObjectQuery()
    {
        var sql = Sql.Builder
            .Select("*")
            .From("Application.People")
            .Where("JSON_VALUE(CustomFields, '$.Title') = @0", "Team Member")
            .OrderBy("PersonID");

        var people = db.Fetch<Person>(sql);

        Assert.Equal(13, people.Count);
        Assert.All(people, person => Assert.Equal("Team Member", person.GetCustomFields()?.Title));

        var first = people.First();
        Assert.Equal("Kayla Woodcock", first.FullName);
        Assert.Equal("Kayla", first.PreferredName);
        Assert.Equal("kaylaw@wideworldimporters.com", first.EmailAddress);
        Assert.Equal(new DateTime(2008, 4, 19), first.GetCustomFields()?.HireDate);
    }

    [Fact]
    public void F2_JSONArrayQuery()
    {
        var sql = Sql.Builder
            .Select("*")
            .From("Application.People")
            .Where("EXISTS (SELECT 1 FROM OPENJSON(OtherLanguages) WHERE value = @0)", "Slovak")
            .OrderBy("PersonID");

        var people = db.Fetch<Person>(sql);

        Assert.Equal(2, people.Count);
        Assert.All(people, person => Assert.Contains("Slovak", person.GetOtherLanguages()!));

        var first = people.First();
        Assert.Equal("Amy Trefl", first.FullName);
        Assert.Equal(["Slovak", "Spanish", "Polish"], first.GetOtherLanguages());
    }

    [Fact]
    public void G1_Union()
    {
        var suppliers = db.Fetch<int>("""
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 5
                UNION
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 10
                ORDER BY SupplierID
            """
        );

        Assert.Equal(10, suppliers.Count);
        Assert.Equal(Enumerable.Range(1, 10), suppliers);
    }

    [Fact]
    public void G2_Intersection()
    {
        var suppliers = db.Fetch<int>("""
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 10
                INTERSECT
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 15
                ORDER BY SupplierID
            """
        );

        Assert.Equal([5, 6, 7, 8, 9], suppliers);
    }

    [Fact]
    public void H1_Metadata()
    {
        var sql = Sql.Builder
            .Select("DATA_TYPE")
            .From("INFORMATION_SCHEMA.COLUMNS")
            .Where("TABLE_SCHEMA = 'Purchasing' AND TABLE_NAME = 'Suppliers' AND COLUMN_NAME = 'SupplierReference'");

        var datatype = db.Single<string>(sql);

        Assert.Equal("nvarchar", datatype);
    }
}
