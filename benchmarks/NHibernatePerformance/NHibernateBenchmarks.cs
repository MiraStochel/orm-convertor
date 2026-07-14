using BenchmarkDotNet.Attributes;
using Common;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Driver;
using NHibernate.Linq;
using NHibernate.Transform;
using NHibernateEntities;
using NHibernateEntities.Models;

namespace NHibernatePerformance
{
    [MemoryDiagnoser]
    [ExceptionDiagnoser]
    public class NHibernateBenchmarks
    {
        private ISessionFactory sessionFactory = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var configuration = new Configuration()
                .DataBaseIntegration(db =>
                {
                    db.ConnectionString = DatabaseConfig.MSSQLConnectionString;
                    db.Driver<SqlClientDriver>();
                    db.Dialect<MsSql2012Dialect>();
                })
                .AddAssembly(typeof(PurchaseOrder).Assembly);

            sessionFactory = configuration.BuildSessionFactory();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            sessionFactory.Dispose();
        }

        [Benchmark]
        public PurchaseOrder A1_EntityIdenticalToTable()
        {
            using var session = sessionFactory.OpenSession();

            var order = session.Get<PurchaseOrder>(25);

            return order;
        }

        [Benchmark]
        public SupplierContactInfo A2_LimitedEntity()
        {
            using var session = sessionFactory.OpenSession();

            var contactInfo = session.Query<Supplier>()
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
            using var session = sessionFactory.OpenSession();

            var result = session.Query<Supplier>()
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
        public IList<System.Collections.Hashtable> A4_StoredProcedureToEntity()
        {
            using var session = sessionFactory.OpenSession();

            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            // Can't map to entity directly, NHibernate throws internal error
            // It can't process the spaces in column names
            var orders = session.GetNamedQuery("GetOrderUpdates")
                .SetDateTime("from", from)
                .SetDateTime("to", to)
                .SetResultTransformer(NHibernate.Transform.Transformers.AliasToEntityMap)
                .List<System.Collections.Hashtable>();

            return orders;
        }

        [Benchmark]
        public List<OrderLine> B1_SelectionOverIndexedColumn()
        {
            using var session = sessionFactory.OpenSession();

            int orderId = 26866;

            var orderLines = session.Query<OrderLine>()
                .Where(ol => ol.OrderID == orderId)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B2_SelectionOverNonIndexedColumn()
        {
            using var session = sessionFactory.OpenSession();

            decimal unitPrice = 25m;

            var orderLines = session.Query<OrderLine>()
                .Where(ol => ol.UnitPrice == unitPrice)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B3_RangeQuery()
        {
            using var session = sessionFactory.OpenSession();

            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = session.Query<OrderLine>()
                .Where(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B4_InQuery()
        {
            using var session = sessionFactory.OpenSession();

            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = session.Query<OrderLine>()
                .Where(ol => orderIds.Contains(ol.OrderID))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B5_TextSearch()
        {
            using var session = sessionFactory.OpenSession();

            string text = "C++";

            var orderLines = session.Query<OrderLine>()
                .Where(ol => ol.Description.Contains(text))
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public List<OrderLine> B6_PagingQuery()
        {
            using var session = sessionFactory.OpenSession();

            int skip = 1000;
            int take = 50;

            var orderLines = session.Query<OrderLine>()
                .OrderBy(ol => ol.OrderLineID)
                .Skip(skip)
                .Take(take)
                .ToList();

            return orderLines;
        }

        [Benchmark]
        public Dictionary<decimal, int> C1_AggregationCount()
        {
            using var session = sessionFactory.OpenSession();

            var taxRates = session.Query<OrderLine>()
                .GroupBy(ol => ol.TaxRate)
                .Select(g => new { TaxRate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.TaxRate, x => x.Count);

            return taxRates;
        }

        [Benchmark]
        public decimal? C2_AggregationMax()
        {
            using var session = sessionFactory.OpenSession();

            var maxUnitPrice = session.Query<OrderLine>()
                .Max(ol => ol.UnitPrice);

            return maxUnitPrice;
        }

        [Benchmark]
        public decimal? C3_AggregationSum()
        {
            using var session = sessionFactory.OpenSession();

            var totalSales = session.Query<OrderLine>()
                .Sum(ol => ol.Quantity * ol.UnitPrice);

            return totalSales;
        }

        [Benchmark]
        public Order D1_OneToManyRelationship()
        {
            using var session = sessionFactory.OpenSession();

            var order = session.Query<Order>()
                .Fetch(o => o.OrderLines)
                .Single(o => o.OrderID == 530);

            return order;
        }

        [Benchmark]
        public (List<StockItem> stockItems, List<StockGroup> stockGroups) D2_ManyToManyRelationship()
        {
            using var session = sessionFactory.OpenSession();

            var stockItems = session.Query<StockItem>()
                .Fetch(si => si.StockGroups)
                .OrderBy(si => si.StockItemID)
                .ToList();

            var stockGroups = session.Query<StockGroup>()
                .Fetch(sg => sg.StockItems)
                .OrderBy(sg => sg.StockGroupID)
                .ToList();

            return (stockItems, stockGroups);
        }

        [Benchmark]
        public List<Customer> D3_OptionalRelationship()
        {
            using var session = sessionFactory.OpenSession();

            var result = session.Query<Customer>()
            .Fetch(c => c.Transactions)
            .OrderBy(c => c.CustomerID)
            .ToList();

            return result;
        }

        [Benchmark]
        public List<PurchaseOrder> E1_ColumnSorting()
        {
            using var session = sessionFactory.OpenSession();

            var orders = session.Query<PurchaseOrder>()
                .OrderBy(po => po.ExpectedDeliveryDate)
                .Take(1000)
                .ToList();

            return orders;
        }

        [Benchmark]
        public List<string?> E2_Distinct()
        {
            using var session = sessionFactory.OpenSession();

            var supplierReferences = session.Query<PurchaseOrder>()
                .Select(po => po.SupplierReference)
                .Distinct()
                .ToList();

            return supplierReferences;
        }

        [Benchmark]
        public IList<Person> F1_JSONObjectQuery()
        {
            using var session = sessionFactory.OpenSession();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages 
                FROM WideWorldImporters.Application.People 
                WHERE JSON_VALUE(CustomFields, '$.Title') = :title
            """;

            var people = session.CreateSQLQuery(sql)
                .SetParameter("title", "Team Member")
                .SetResultTransformer(Transformers.AliasToBean<Person>())
                .List<Person>();

            return people;
        }

        [Benchmark]
        public IList<Person> F2_JSONArrayQuery()
        {
            using var session = sessionFactory.OpenSession();

            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages 
                FROM WideWorldImporters.Application.People 
                WHERE EXISTS (
                    SELECT 1 FROM OPENJSON(OtherLanguages) 
                    WHERE value = :lang
                )
            """;

            var people = session.CreateSQLQuery(sql)
                .SetParameter("lang", "Slovak")
                .SetResultTransformer(Transformers.AliasToBean<Person>())
                .List<Person>();

            return people;
        }

        [Benchmark]
        public List<int> G1_Union()
        {
            using var session = sessionFactory.OpenSession();

            var first = session.Query<Supplier>()
                .Where(s => s.SupplierID < 5)
                .Select(s => s.SupplierID)
                .ToList();

            var last = session.Query<Supplier>()
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
            using var session = sessionFactory.OpenSession();

            var first = session.Query<Supplier>()
                .Where(s => s.SupplierID < 10)
                .Select(s => s.SupplierID)
                .ToList();

            var last = session.Query<Supplier>()
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
            using var session = sessionFactory.OpenSession();

            var datatype = session.CreateSQLQuery(
                """
                    SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS 
                        WHERE TABLE_SCHEMA = 'Purchasing'
                        AND TABLE_NAME = 'Suppliers'
                        AND COLUMN_NAME = 'SupplierReference'
                """
            )
            .UniqueResult<string>();

            return datatype;
        }
    }
}
