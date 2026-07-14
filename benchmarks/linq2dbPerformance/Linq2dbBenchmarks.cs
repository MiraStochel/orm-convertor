using BenchmarkDotNet.Attributes;
using Common;
using linq2dbEntities;
using linq2dbEntities.Models;
using LinqToDB;
using LinqToDB.Data;

namespace Linq2dbPerformance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class Linq2dbBenchmarks
    {
        private WWIDbConnection GetConnection()
        {
            var options = new DataOptions()
                .UseSqlServer(DatabaseConfig.MSSQLConnectionString);

            return new WWIDbConnection(options);
        }

        [Benchmark]
        public PurchaseOrder? A1_EntityIdenticalToTable()
        {
            using var db = GetConnection();

            var order = db.PurchaseOrders.FirstOrDefault(x => x.PurchaseOrderID == 25);

            return order;
        }

        [Benchmark]
        public SupplierContactInfo A2_LimitedEntity()
        {
            using var db = GetConnection();

            var contactInfo = db.Suppliers
            .Where(s => s.SupplierID == 10)
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
            using var db = GetConnection();

            var result = db.Suppliers
            .Where(s => s.SupplierID == 10)
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
            using var db = GetConnection();

            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            var orders = db.QueryProc<PurchaseOrderUpdate>(
                    "WideWorldImporters.Integration.GetOrderUpdates",
                    new DataParameter("LastCutoff", from),
                    new DataParameter("NewCutoff", to)
                ).ToList();

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            using var db = GetConnection();

            int orderId = 26866;

            var orderLines = db.OrderLines
            .Where(ol => ol.OrderID == orderId)
            .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            using var db = GetConnection();

            decimal unitPrice = 25m;

            var orderLines = db.OrderLines
                .Where(ol => ol.UnitPrice == unitPrice)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            using var db = GetConnection();

            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = db.OrderLines
                .Where(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to)
            .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B4_InQuery()
        {
            using var db = GetConnection();

            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = db.OrderLines
                .Where(ol => orderIds.Contains(ol.OrderID))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            using var db = GetConnection();

            string text = "C++";

            var orderLines = db.OrderLines
                .Where(ol => ol.Description.Contains(text))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            using var db = GetConnection();

            int skip = 1000;
            int take = 50;

            var orderLines = db.OrderLines
                .OrderBy(ol => ol.OrderLineID)
                .Skip(skip)
            .Take(take)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public Dictionary<decimal, int> C1_AggregationCount()
        {
            using var db = GetConnection();

            var taxRates = db.OrderLines
                .GroupBy(ol => ol.TaxRate)
                .Select(g => new { TaxRate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.TaxRate, x => x.Count);

            return taxRates;
        }

        [Benchmark]
        public decimal? C2_AggregationMax()
        {
            using var db = GetConnection();

            var maxUnitPrice = db.OrderLines.Max(ol => ol.UnitPrice);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal? C3_AggregationSum()
        {
            using var db = GetConnection();

            var totalSales = db.OrderLines
                .Sum(ol => ol.Quantity * ol.UnitPrice);

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
        {
            using var db = GetConnection();

            var order = db.Orders
                .LoadWith(o => o.OrderLines)
                .Single(o => o.OrderID == 530);

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
        {
            using var db = GetConnection();

            var stockItems = db.StockItems
                .LoadWith(si => si.StockGroups)
                .ThenLoad(sisg => sisg.StockGroup)
                .OrderBy(si => si.StockItemID)
                .ToList();

            var stockGroups = db.StockGroups
                .LoadWith(sg => sg.StockItems)
                .ThenLoad(sisg => sisg.StockItem)
                .OrderBy(si => si.StockGroupID)
                .ToList();

            return (stockItems, stockGroups);
        }

        [Benchmark]
        public List<Customer> D3_OptionalRelationship()
        {
            using var db = GetConnection();

            var result = db.Customers
            .LoadWith(c => c.Transactions)
            .OrderBy(c => c.CustomerID)
            .ToList();

            return result;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            using var db = GetConnection();

            var orders = db.PurchaseOrders
                .OrderBy(po => po.ExpectedDeliveryDate)
            .Take(1000)
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<string?> E2_Distinct()
        {
            using var db = GetConnection();

            var supplierReferences = db.PurchaseOrders
                .Select(po => po.SupplierReference)
            .Distinct()
            .ToList();

            return supplierReferences;
        }

        [Benchmark]
        public List<Person> F1_JSONObjectQuery()
        {
            using var db = GetConnection();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE JSON_VALUE(CustomFields, '$.Title') = @Title
                ORDER BY PersonID
            """;

            var people = db.Query<Person>(
                sql,
                new DataParameter("Title", "Team Member")
            ).ToList();

            return people;
        }

        [Benchmark]
        public List<Person> F2_JSONArrayQuery()
        {
            using var db = GetConnection();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE EXISTS (
                    SELECT 1
                    FROM OPENJSON(OtherLanguages)
                    WHERE value = @Language
                )
            """;

            var people = db.Query<Person>(
                sql,
                new DataParameter("Language", "Slovak")
            ).ToList();

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            using var db = GetConnection();

            var first = db.Suppliers
                .Where(s => s.SupplierID < 5)
                .Select(s => s.SupplierID)
                .ToList();

            var last = db.Suppliers
                .Where(s => s.SupplierID >= 5 && s.SupplierID <= 10)
                .Select(s => s.SupplierID)
                .ToList();

            var suppliers = first
                .Union(last)
                .OrderBy(s => s)
            .ToList();

            return suppliers;
        }

        [Benchmark]
        public List<int> G2_Intersection()
        {
            using var db = GetConnection();

            var first = db.Suppliers
                .Where(s => s.SupplierID < 10)
                .Select(s => s.SupplierID)
                .ToList();

            var last = db.Suppliers
                .Where(s => s.SupplierID >= 5 && s.SupplierID <= 15)
                .Select(s => s.SupplierID)
                .ToList();

            var suppliers = first
                .Intersect(last)
                .OrderBy(s => s)
            .ToList();

            return suppliers;
        }

        [Benchmark]
        public string H1_Metadata()
        {
            using var db = GetConnection();

            var datatype = db.Query<string>(
                """
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
