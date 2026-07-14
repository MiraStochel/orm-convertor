using System.ComponentModel;
using System.Data;
using System.Data.Entity;
using BenchmarkDotNet.Attributes;
using EF6Entities;
using EF6Entities.Models;

namespace EF6Performance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class EF6Benchmarks
    {
        private WWIContext GetContext()
        {
            var context = new WWIContext();
            context.Configuration.AutoDetectChangesEnabled = false;
            context.Configuration.ProxyCreationEnabled = false;
            context.Configuration.LazyLoadingEnabled = false;
            return context;
        }

        [Benchmark]
        public PurchaseOrder A1_EntityIdenticalToTable()
        {
            using var context = GetContext();

            var order = context.PurchaseOrders.Find(25);

            return order;
        }

        [Benchmark]
        public SupplierContactInfo A2_LimitedEntity()
        {
            using var context = GetContext();

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
            using var context = GetContext();

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
        [Description("FAIL - EF6 cannot be used.")]
        public List<PurchaseOrderUpdate> A4_StoredProcedureToEntity()
        {
            using var context = GetContext();

            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            var connection = context.Database.Connection;
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();

            command.CommandText = "WideWorldImporters.Integration.GetOrderUpdates";
            command.CommandType = CommandType.StoredProcedure;

            var paramLastCutoff = command.CreateParameter();
            paramLastCutoff.ParameterName = "@LastCutoff";
            paramLastCutoff.Value = from;
            command.Parameters.Add(paramLastCutoff);

            var paramNewCutoff = command.CreateParameter();
            paramNewCutoff.ParameterName = "@NewCutoff";
            paramNewCutoff.Value = to;
            command.Parameters.Add(paramNewCutoff);

            var orders = new List<PurchaseOrderUpdate>();

            // EF6 cannot properly execute stored procedures,
            // especially in our case where the SP returns columns with spaces in their names.
            // There is no workaround for this, except modifying the procedure, which we don't want to do.
            // So we use ADO.NET to execute the stored procedure and map the results to our entity.
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var order = new PurchaseOrderUpdate
                {
                    OrderID = reader["WWI Order ID"] != DBNull.Value ? Convert.ToInt32(reader["WWI Order ID"]) : 0,
                    Description = reader["Description"] != DBNull.Value ? reader["Description"].ToString() : string.Empty,
                    Quantity = reader["Quantity"] != DBNull.Value ? Convert.ToInt32(reader["Quantity"]) : 0,
                    UnitPrice = reader["Unit Price"] != DBNull.Value ? Convert.ToDecimal(reader["Unit Price"]) : 0m,
                    TaxRate = reader["Tax Rate"] != DBNull.Value ? Convert.ToDecimal(reader["Tax Rate"]) : 0m,
                    TotalIncludingTax = reader["Total Including Tax"] != DBNull.Value ? Convert.ToDecimal(reader["Total Including Tax"]) : 0m
                };

                orders.Add(order);
            }

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            using var context = GetContext();

            int orderId = 26866;

            var orderLines = context.OrderLines
                .Where(ol => ol.OrderID == orderId)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            using var context = GetContext();

            decimal unitPrice = 25m;

            var orderLines = context.OrderLines
                .Where(ol => ol.UnitPrice == unitPrice)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            using var context = GetContext();

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
            using var context = GetContext();

            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = context.OrderLines
                .Where(ol => orderIds.Contains(ol.OrderID))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            using var context = GetContext();

            string text = "C++";

            var orderLines = context.OrderLines
                .Where(ol => ol.Description.Contains(text))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            using var context = GetContext();

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
            using var context = GetContext();

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
            using var context = GetContext();

            var maxUnitPrice = context.OrderLines.Max(ol => ol.UnitPrice);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal? C3_AggregationSum()
        {
            using var context = GetContext();

            var totalSales = context.OrderLines
                .Sum(ol => ol.Quantity * ol.UnitPrice);

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
        {
            using var context = GetContext();

            var order = context.Orders
                .Include(o => o.OrderLines)
                .Single(o => o.OrderID == 530);

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
        {
            using var context = GetContext();

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
            using var context = GetContext();

            var result = context.Customers
            .Include(c => c.Transactions)
            .OrderBy(c => c.CustomerID)
            .ToList();

            return result;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            using var context = GetContext();

            var orders = context.PurchaseOrders
                .OrderBy(po => po.ExpectedDeliveryDate)
                .Take(1000)
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<string?> E2_Distinct()
        {
            using var context = GetContext();

            var supplierReferences = context.PurchaseOrders
                .Select(po => po.SupplierReference)
                .Distinct()
                .ToList();

            return supplierReferences;
        }

        [Benchmark]
        public List<Person> F1_JSONObjectQuery()
        {
            using var context = GetContext();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE JSON_VALUE(CustomFields, '$.Title') = @p0
                ORDER BY PersonID
            """;

            var people = context.Database.SqlQuery<Person>(
                sql,
                "Team Member"
            ).ToList();

            return people;
        }

        [Benchmark]
        public List<Person> F2_JSONArrayQuery()
        {
            using var context = GetContext();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE EXISTS (
                    SELECT 1
                    FROM OPENJSON(OtherLanguages)
                    WHERE value = @p0
                )
            """;

            var people = context.Database.SqlQuery<Person>(
                sql,
                "Slovak"
            )
            .ToList();

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            using var context = GetContext();

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
            using var context = GetContext();

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
            using var context = GetContext();

            var datatype = context.Database.SqlQuery<string>(
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
