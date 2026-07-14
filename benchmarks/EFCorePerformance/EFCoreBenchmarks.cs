using BenchmarkDotNet.Attributes;
using Common;
using EFCoreEntities;
using EFCoreEntities.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EFCorePerformance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class EFCoreBenchmarks
    {
        private PooledDbContextFactory<WWIContext> contextFactory = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var options = new DbContextOptionsBuilder<WWIContext>()
           .UseSqlServer(DatabaseConfig.MSSQLConnectionString)
           // We only test read queries, tracking is not needed and might slow down the tests
           .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
           .ConfigureWarnings(warnings =>
           {
               warnings.Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning);
               // we only read the values, so we don't need to worry about precision when saving
               warnings.Ignore(SqlServerEventId.DecimalTypeDefaultWarning);
           })
           .Options;

            contextFactory = new(options);
        }

        [GlobalCleanup]
        public void GlobalCleanup() {}

        [Benchmark]
        public PurchaseOrder? A1_EntityIdenticalToTable()
        {
            using var context = contextFactory.CreateDbContext();

            var order = context.PurchaseOrders.Find(25);

            return order;
        }

        [Benchmark]
        public SupplierContactInfo A2_LimitedEntity()
        {
            using var context = contextFactory.CreateDbContext();

            var contactInfo = context.Suppliers
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
            using var context = contextFactory.CreateDbContext();

            var result = context.Suppliers
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
            using var context = contextFactory.CreateDbContext();

            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            var orders = context.Set<PurchaseOrderUpdate>()
                .FromSqlInterpolated($"EXEC WideWorldImporters.Integration.GetOrderUpdates @LastCutoff = {from}, @NewCutoff = {to}")
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            using var context = contextFactory.CreateDbContext();

            int orderId = 26866;

            var orderLines = context.OrderLines
                .Where(ol => ol.OrderID == orderId)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            using var context = contextFactory.CreateDbContext();

            decimal unitPrice = 25m;

            var orderLines = context.OrderLines
                .Where(ol => ol.UnitPrice == unitPrice)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            using var context = contextFactory.CreateDbContext();

            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = context.OrderLines
                .Where(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B4_InQuery()
        {
            using var context = contextFactory.CreateDbContext();

            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = context.OrderLines
                .Where(ol => orderIds.Contains(ol.OrderID))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            using var context = contextFactory.CreateDbContext();

            string text = "C++";

            var orderLines = context.OrderLines
                //.Where(ol => EF.Functions.Like(ol.Description, $"%{text}%"))
                .Where(ol => ol.Description.Contains(text))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            using var context = contextFactory.CreateDbContext();

            int skip = 1000;
            int take = 50;

            var orderLines = context.OrderLines
                .OrderBy(ol => ol.OrderLineID)
                .Skip(skip)
                .Take(take)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public Dictionary<decimal, int> C1_AggregationCount()
        {
            using var context = contextFactory.CreateDbContext();

            var taxRates = context.OrderLines
                .GroupBy(ol => ol.TaxRate)
                .Select(g => new { TaxRate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.TaxRate, x => x.Count);

            return taxRates;
        }

        [Benchmark]
        public decimal? C2_AggregationMax()
        {
            using var context = contextFactory.CreateDbContext();

            var maxUnitPrice = context.OrderLines.Max(ol => ol.UnitPrice);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal? C3_AggregationSum()
        {
            using var context = contextFactory.CreateDbContext();

            var totalSales = context.OrderLines
                .Sum(ol => ol.Quantity * ol.UnitPrice);

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
        {
            using var context = contextFactory.CreateDbContext();

            var order = context.Orders
                .Include(o => o.OrderLines)
                .Single(o => o.OrderID == 530);

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
        {
            using var context = contextFactory.CreateDbContext();

            var stockItems = context.StockItems
                .Include(si => si.StockGroups)
                .OrderBy(si => si.StockItemID)
                .ToList();

            var stockGroups = context.StockGroups
                .Include(sg => sg.StockItems)
                .OrderBy(si => si.StockGroupID)
                .ToList();

            return (stockItems, stockGroups);
        }

        [Benchmark]
        public List<Customer> D3_OptionalRelationship()
        {
            using var context = contextFactory.CreateDbContext();

            var result = context.Customers
            .Include(c => c.Transactions)
            .OrderBy(c => c.CustomerID)
            .ToList();

            return result;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            using var context = contextFactory.CreateDbContext();

            var orders = context.PurchaseOrders
                .OrderBy(po => po.ExpectedDeliveryDate)
                .Take(1000)
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<string?> E2_Distinct()
        {
            using var context = contextFactory.CreateDbContext();

            var supplierReferences = context.PurchaseOrders
                .Select(po => po.SupplierReference)
                .Distinct()
                .ToList();

            return supplierReferences;
        }

        [Benchmark]
        public List<Person> F1_JSONObjectQuery()
        {
            using var context = contextFactory.CreateDbContext();

            var people = context.People
                .Where(p => p.CustomFields!.Title == "Team Member")
                .OrderBy(p => p.PersonID)
                .ToList();

            return people;
        }

        [Benchmark]
        public List<Person> F2_JSONArrayQuery()
        {
            using var context = contextFactory.CreateDbContext();

            var people = context.People
                .Where(p => p.OtherLanguages!.Contains("Slovak"))
                .OrderBy(p => p.PersonID)
                .ToList();

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            using var context = contextFactory.CreateDbContext();

            var first = context.Suppliers
                .Where(s => s.SupplierID < 5)
                .Select(s => s.SupplierID)
                .ToList();

            var last = context.Suppliers
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
            using var context = contextFactory.CreateDbContext();

            var first = context.Suppliers
                .Where(s => s.SupplierID < 10)
                .Select(s => s.SupplierID)
                .ToList();

            var last = context.Suppliers
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
            using var context = contextFactory.CreateDbContext();

            var datatype = context.Database.SqlQueryRaw<string>(
                """
                    SELECT DATA_TYPE AS Value FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_SCHEMA = 'Purchasing'
                        AND TABLE_NAME = 'Suppliers'
                        AND COLUMN_NAME = 'SupplierReference'
                """
            ).Single();

            return datatype;
        }
    }
}
