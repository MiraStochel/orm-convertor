using BenchmarkDotNet.Attributes;
using Common;
using PetaPoco.Providers;
using PetaPoco;
using PetaPocoEntities;

namespace PetaPocoPerformance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class PetaPocoBenchmark
    {
        private IDatabase db = default!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            db = DatabaseConfiguration.Build()
                .UsingConnectionString(DatabaseConfig.MSSQLConnectionString)
                .UsingProvider<SqlServerDatabaseProvider>()
                .UsingDefaultMapper<ConventionMapper>(m =>
                {
                    m.InflectTableName = (inflector, s) => inflector.Pluralise(s);
                    m.InflectColumnName = (inflector, s) => s;
                })
                .Create();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            db.Dispose();
        }

        [Benchmark]
        public PurchaseOrder A1_EntityIdenticalToTable()
        {
            var order = db.Single<PurchaseOrder>(
                "SELECT * FROM WideWorldImporters.Purchasing.PurchaseOrders WHERE PurchaseOrderId = @PurchaseOrderId",
                new { PurchaseOrderId = 25 }
            );

            return order;
        }

        [Benchmark]

        public SupplierContactInfo A2_LimitedEntity()
        {
            var contactInfo = db.Single<SupplierContactInfo>(
                "WHERE SupplierID = @SupplierID",
                new { SupplierID = 10 }
            );

            return contactInfo;
        }

        [Benchmark]
        public (SupplierContactInfo, SupplierBankAccount) A3_MultipleEntitiesFromOneResult()
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

            return result.Single();
        }

        [Benchmark]
        public List<PurchaseOrderUpdate> A4_StoredProcedureToEntity()
        {
            var orders = db.FetchProc<PurchaseOrderUpdate>(
                "WideWorldImporters.Integration.GetOrderUpdates",
                new { LastCutoff = new DateTime(2014, 1, 1), NewCutoff = new DateTime(2015, 1, 1) }
            );

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            int orderId = 26866;

            var orderLines = db.Fetch<OrderLine>(
                Sql.Builder.Where("OrderID = @0", orderId)
            );

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            decimal unitPrice = 25m;

            var orderLines = db.Fetch<OrderLine>(
                Sql.Builder.Where("UnitPrice = @0", unitPrice)
            );

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = db.Fetch<OrderLine>(
                Sql.Builder.Where("PickingCompletedWhen BETWEEN @0 AND @1", from, to)
            );

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B4_InQuery()
        {
            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = db.Fetch<OrderLine>(
                Sql.Builder.Where("OrderID IN (@0)", orderIds)
            );

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            string text = "C++";

            var orderLines = db.Fetch<OrderLine>(
                Sql.Builder.Where($"Description LIKE @0", $"%{text}%")
            );

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            int skip = 1000;
            int take = 50;

            var orderLines = db.Fetch<OrderLine>(
                "ORDER BY OrderLineID OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY",
                new { Skip = skip, Take = take }
            );

            return orderLines;
        }

        [Benchmark]
        public Dictionary<decimal, int> C1_AggregationCount()
        {
            var sql = Sql.Builder
                .Select("TaxRate", "COUNT(TaxRate) as Count")
                .From("Sales.OrderLines")
                .GroupBy("TaxRate")
                .OrderBy("Count DESC");

            var taxRates = db
                .Fetch<dynamic>(sql)
                .ToDictionary(x => (decimal)x.TaxRate, x => (int)x.Count);

            return taxRates;
        }

        [Benchmark]
        public decimal C2_AggregationMax()
        {
            var sql = Sql.Builder
                .Select("MAX(UnitPrice)")
                .From("Sales.OrderLines");

            var maxUnitPrice = db.Single<decimal>(sql);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal C3_AggregationSum()
        {
            var sql = Sql.Builder
                .Select("SUM(Quantity * UnitPrice)")
                .From("Sales.OrderLines");

            var totalSales = db.Single<decimal>(sql);

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
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

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
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

            return (stockItems, stockGroups);
        }

        [Benchmark]
        public List<Customer> D3_OptionalRelationship()
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

            return result;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            var sql = Sql.Builder
                .Select("TOP (1000) *")
                .From("Purchasing.PurchaseOrders")
                .OrderBy("ExpectedDeliveryDate ASC");

            var orders = db.Fetch<PurchaseOrder>(sql);

            return orders;
        }

        [Benchmark]
        public List<string> E2_Distinct()
        {
            var sql = Sql.Builder
                .Select("DISTINCT SupplierReference")
                .From("Purchasing.PurchaseOrders");

            var supplierReferences = db.Fetch<string>(sql);

            return supplierReferences;
        }

        [Benchmark]
        public List<Person> F1_JSONObjectQuery()
        {
            var sql = Sql.Builder
                .Select("*")
                .From("Application.People")
                .Where("JSON_VALUE(CustomFields, '$.Title') = @0", "Team Member")
                .OrderBy("PersonID");

            var people = db.Fetch<Person>(sql);

            return people;
        }

        [Benchmark]
        public List<Person> F2_JSONArrayQuery()
        {
            var sql = Sql.Builder
                .Select("*")
                .From("Application.People")
                .Where("EXISTS (SELECT 1 FROM OPENJSON(OtherLanguages) WHERE value = @0)", "Slovak")
                .OrderBy("PersonID");

            var people = db.Fetch<Person>(sql);

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            var suppliers = db.Fetch<int>("""
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 5
                UNION
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 10
                ORDER BY SupplierID
            """
            );

            return suppliers;
        }

        [Benchmark]
        public List<int> G2_Intersection()
        {
            var suppliers = db.Fetch<int>("""
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 10
                INTERSECT
                SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 15
                ORDER BY SupplierID
            """
            );

            return suppliers;
        }

        [Benchmark]
        public string H1_Metadata()
        {
            var sql = Sql.Builder
                .Select("DATA_TYPE")
                .From("INFORMATION_SCHEMA.COLUMNS")
                .Where("TABLE_SCHEMA = 'Purchasing' AND TABLE_NAME = 'Suppliers' AND COLUMN_NAME = 'SupplierReference'");

            var datatype = db.Single<string>(sql);

            return datatype;
        }
    }
}
