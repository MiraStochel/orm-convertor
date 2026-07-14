using System.Data;
using BenchmarkDotNet.Attributes;
using Common;
using Microsoft.Data.SqlClient;
using RepoDb;
using RepoDb.Extensions;
using RepoDBEntities;
using RepoDBEntities.Models;

namespace RepoDBPerformance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class RepoDBBenchmark
    {
        private SqlConnection connection = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            GlobalConfiguration
            .Setup(new()
            {
                DefaultCacheItemExpirationInMinutes = 0
            })
            .UseSqlServer();

            connection = new(DatabaseConfig.MSSQLConnectionString);
            connection.Open();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            connection.Dispose();
        }

        [Benchmark]
        public PurchaseOrder A1_EntityIdenticalToTable()
        {
            var order = connection.Query<PurchaseOrder>(po => po.PurchaseOrderID == 25).Single();

            return order;
        }

        [Benchmark]
        public SupplierContactInfo A2_LimitedEntity()
        {
            var contactInfo = connection.Query<Supplier>(
                s => s.SupplierID == 10,
                Field.From("SupplierID", "SupplierName", "PhoneNumber", "FaxNumber", "WebsiteURL", "ValidFrom", "ValidTo")
            )
            .Select(s => new SupplierContactInfo
            {
                SupplierID = s.SupplierID,
                SupplierName = s.SupplierName,
                PhoneNumber = s.PhoneNumber,
                FaxNumber = s.FaxNumber,
                WebsiteURL = s.WebsiteURL,
                ValidFrom = s.ValidFrom,
                ValidTo = s.ValidTo
            })
            .Single();

            return contactInfo;
        }

        [Benchmark]
        public (SupplierContactInfo ContactInfo, SupplierBankAccount BankAccount) A3_MultipleEntitiesFromOneResult()
        {
            var result = connection.Query<Supplier>(
                s => s.SupplierID == 10,
                Field.From(
                    "SupplierID", "SupplierName", "PhoneNumber", "FaxNumber", "WebsiteURL", "ValidFrom", "ValidTo",
                    "BankAccountName", "BankAccountBranch", "BankAccountCode", "BankAccountNumber", "BankInternationalCode"
                )
            )
            .Select(s => new
            {
                ContactInfo = new SupplierContactInfo
                {
                    SupplierID = s.SupplierID,
                    SupplierName = s.SupplierName,
                    PhoneNumber = s.PhoneNumber,
                    FaxNumber = s.FaxNumber,
                    WebsiteURL = s.WebsiteURL,
                    ValidFrom = s.ValidFrom,
                    ValidTo = s.ValidTo
                },
                BankAccount = new SupplierBankAccount
                {
                    SupplierID = s.SupplierID,
                    BankAccountName = s.BankAccountName,
                    BankAccountBranch = s.BankAccountBranch,
                    BankAccountCode = s.BankAccountCode,
                    BankAccountNumber = s.BankAccountNumber,
                    BankInternationalCode = s.BankInternationalCode
                }
            })
            .Select(result => new { result.ContactInfo, result.BankAccount })
            .Single();

            return (result.ContactInfo, result.BankAccount);
        }

        [Benchmark]
        public List<PurchaseOrderUpdate> A4_StoredProcedureToEntity()
        {
            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            var orders = connection.ExecuteQuery<PurchaseOrderUpdate>(
                "WideWorldImporters.Integration.GetOrderUpdates",
                new { LastCutoff = from, NewCutoff = to },
                CommandType.StoredProcedure
            ).ToList();

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            int orderId = 26866;

            var orderLines = connection.Query<OrderLine>(ol => ol.OrderID == orderId).ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            decimal unitPrice = 25m;

            var orderLines = connection.Query<OrderLine>(ol => ol.UnitPrice == unitPrice).ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = connection.Query<OrderLine>(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to).ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B4_InQuery()
        {
            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = connection.Query<OrderLine>(ol => orderIds.Contains(ol.OrderID)).ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            string text = "C++";

            var orderLines = connection.Query<OrderLine>(ol => ol.Description.Contains(text)).ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            int skip = 1000;
            int take = 50;
            var orderBy = OrderField.Ascending<OrderLine>(ol => ol.OrderLineID).AsEnumerable();

            var orderLines = connection.SkipQuery<OrderLine>(skip, take, orderBy, where: default(object)!).ToList();

            return orderLines;
        }

        [Benchmark]
        public Dictionary<decimal, int> C1_AggregationCount()
        {
            var taxRates = connection.QueryAll<OrderLine>()
                .GroupBy(ol => ol.TaxRate)
                .Select(g => new { TaxRate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.TaxRate, x => x.Count);

            return taxRates;
        }

        [Benchmark]
        public decimal? C2_AggregationMax()
        {
            var maxUnitPrice = connection.MaxAll<OrderLine, decimal?>(ol => ol.UnitPrice);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal C3_AggregationSum()
        {
            var totalSales = connection.ExecuteQuery<decimal>(
                "SELECT SUM(Quantity * UnitPrice) FROM WideWorldImporters.Sales.OrderLines"
            ).Single();

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
        {
            var result = connection.QueryMultiple<Order, OrderLine>(
                o => o.OrderID == 530,
                ol => ol.OrderID == 530
            );

            var order = result.Item1.Single();
            order.OrderLines = result.Item2.ToList();

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
        {
            // RepoDB has no support for relationships or joins whatsoever

            string sqlItems = """
                SELECT si.StockItemID,
                    si.StockItemName,
                    si.SupplierID,
                    si.Brand, 
                    si.Size,
                    sg.StockGroupID,
                    sg.StockGroupName
                FROM WideWorldImporters.Warehouse.StockItems si
                LEFT JOIN WideWorldImporters.Warehouse.StockItemStockGroups sisg
                    ON si.StockItemID = sisg.StockItemID
                LEFT JOIN WideWorldImporters.Warehouse.StockGroups sg
                    ON sisg.StockGroupID = sg.StockGroupID
                ORDER BY si.StockItemID
            """;

            var rawItems = connection.ExecuteQuery<dynamic>(sqlItems).ToList();
            var stockItemsById = new Dictionary<int, StockItem>();

            foreach (var row in rawItems)
            {
                if (!stockItemsById.TryGetValue(row.StockItemID, out StockItem stockItem))
                {
                    stockItem = new StockItem
                    {
                        StockItemID = row.StockItemID,
                        StockItemName = row.StockItemName,
                        SupplierID = row.SupplierID
                    };
                    stockItemsById.Add(stockItem.StockItemID, stockItem);
                }

                if (row.StockGroupID != null)
                {
                    stockItem.StockGroups.Add(new StockGroup
                    {
                        StockGroupID = row.StockGroupID,
                        StockGroupName = row.StockGroupName
                    });
                }
            }

            var stockItems = stockItemsById.Values.ToList();

            string sqlGroups = """
                SELECT sg.StockGroupID,
                    sg.StockGroupName,
                    si.StockItemID,
                    si.StockItemName,
                    si.SupplierID,
                    si.Brand, 
                    si.Size
                FROM WideWorldImporters.Warehouse.StockGroups sg
                LEFT JOIN WideWorldImporters.Warehouse.StockItemStockGroups sisg
                    ON sg.StockGroupID = sisg.StockGroupID
                LEFT JOIN WideWorldImporters.Warehouse.StockItems si
                    ON sisg.StockItemID = si.StockItemID
                ORDER BY sg.StockGroupID
            """;

            var rawGroups = connection.ExecuteQuery<dynamic>(sqlGroups).ToList();
            var stockGroupsById = new Dictionary<int, StockGroup>();

            foreach (var row in rawGroups)
            {
                if (!stockGroupsById.TryGetValue(row.StockGroupID, out StockGroup stockGroup))
                {
                    stockGroup = new StockGroup
                    {
                        StockGroupID = row.StockGroupID,
                        StockGroupName = row.StockGroupName
                    };
                    stockGroupsById.Add(stockGroup.StockGroupID, stockGroup);
                }

                if (row.StockItemID != null)
                {
                    stockGroup.StockItems.Add(new StockItem
                    {
                        StockItemID = row.StockItemID,
                        StockItemName = row.StockItemName,
                        SupplierID = row.SupplierID
                    });
                }
            }

            var stockGroups = stockGroupsById.Values.ToList();

            return (stockItems, stockGroups);
        }

        [Benchmark]
        public List<Customer> D3_OptionalRelationship()
        {
            var result = connection.QueryMultiple<Customer, CustomerTransaction>(
                c => c.CustomerID != 0, ct => ct.CustomerID != 0
            );

            var customers = result.Item1.ToList();

            foreach (var t in result.Item2)
            {
                if (t.CustomerTransactionID != default)
                {
                    customers.Single(c => c.CustomerID == t.CustomerID).Transactions.Add(t);
                }
            }

            return customers;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            var orderBy = OrderField.Ascending<PurchaseOrder>(po => po.ExpectedDeliveryDate!).AsEnumerable();

            var orders = connection.SkipQuery<PurchaseOrder>(0, 1000, orderBy, default(object)!)
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<string> E2_Distinct()
        {
            var supplierReferences = connection.ExecuteQuery<string>(
                "SELECT DISTINCT SupplierReference FROM WideWorldImporters.Purchasing.PurchaseOrders"
            ).ToList();

            return supplierReferences;
        }

        [Benchmark]
        public List<Person> F1_JSONObjectQuery()
        {
            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE JSON_VALUE(CustomFields, '$.Title') = @Title
                ORDER BY PersonID
            """;

            var people = connection.ExecuteQuery<Person>(sql, new { Title = "Team Member" }).ToList();

            return people;
        }

        [Benchmark]
        public List<Person> F2_JSONArrayQuery()
        {
            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE EXISTS (
                    SELECT 1
                    FROM OPENJSON(OtherLanguages)
                    WHERE value = @Language
                )
            """;

            var people = connection.ExecuteQuery<Person>(sql, new { Language = "Slovak" }).ToList();

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            var suppliers = connection.ExecuteQuery<int>("""
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 5
                    UNION
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 10
                    ORDER BY SupplierID
                """
            ).ToList();

            return suppliers;
        }

        [Benchmark]
        public List<int> G2_Intersection()
        {
            var suppliers = connection.ExecuteQuery<int>("""
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 10
                    INTERSECT
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 15
                    ORDER BY SupplierID
                """
            ).ToList();

            return suppliers;
        }

        [Benchmark]
        public string H1_Metadata()
        {
            var datatype = connection.ExecuteQuery<string>("""
                SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = 'Purchasing'
                    AND TABLE_NAME = 'Suppliers'
                    AND COLUMN_NAME = 'SupplierReference'
            """
            ).Single();

            return datatype;
        }
    }
}
