using System.Data;
using Common;
using Microsoft.Data.SqlClient;
using RepoDb;
using RepoDb.Extensions;
using RepoDBEntities;
using RepoDBEntities.Models;

namespace RepoDBFeatures
{
    [Collection("PetaPoco")]
    public class FeatureTests
    {
        private readonly SqlConnection connection = new(DatabaseConfig.MSSQLConnectionString);

        static FeatureTests()
        {
            GlobalConfiguration
            .Setup(new()
            {
                DefaultCacheItemExpirationInMinutes = 0
            })
            .UseSqlServer();
        }

        [Fact] 
        public void A1_EntityIdenticalToTable()
        {
            var order = connection.Query<PurchaseOrder>(po => po.PurchaseOrderID == 25).Single();

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

            var (contactInfo, bankAccount) = (result.ContactInfo, result.BankAccount);

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
            var from = new DateTime(2014, 1, 1);
            var to = new DateTime(2015, 1, 1);

            var orders = connection.ExecuteQuery<PurchaseOrderUpdate>(
                "WideWorldImporters.Integration.GetOrderUpdates",
                new { LastCutoff = from, NewCutoff = to },
                CommandType.StoredProcedure
            ).ToList();

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

            var orderLines = connection.Query<OrderLine>(ol => ol.OrderID == orderId).ToList();

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

            var orderLines = connection.Query<OrderLine>(ol => ol.UnitPrice == unitPrice).ToList();

            Assert.Equal(13553, orderLines.Count);
            Assert.True(orderLines.All(ol => ol.UnitPrice == unitPrice));
        }

        [Fact]
        public void B3_RangeQuery()
        {
            var from = new DateTime(2014, 12, 20);
            var to = new DateTime(2014, 12, 31);

            var orderLines = connection.Query<OrderLine>(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to).ToList();

            Assert.Equal(1883, orderLines.Count);
            Assert.True(orderLines.All(ol => ol.PickingCompletedWhen >= from && ol.PickingCompletedWhen <= to));
        }

        [Fact]
        public void B4_InQuery()
        {
            var orderIds = new[] { 1, 10, 100, 1000, 10000 };

            var orderLines = connection.Query<OrderLine>(ol => orderIds.Contains(ol.OrderID)).ToList();

            Assert.Equal(15, orderLines.Count);
            Assert.True(orderLines.All(ol => orderIds.Contains(ol.OrderID)));
        }

        [Fact]
        public void B5_TextSearch()
        {
            string text = "C++";

            var orderLines = connection.Query<OrderLine>(ol => ol.Description.Contains(text)).ToList();

            Assert.Equal(2098, orderLines.Count);
            Assert.True(orderLines.All(ol => ol.Description.Contains(text)));
        }

        [Fact]
        public void B6_PagingQuery()
        {
            int skip = 1000;
            int take = 50;
            var orderBy = OrderField.Ascending<OrderLine>(ol => ol.OrderLineID).AsEnumerable();

            var orderLines = connection.SkipQuery<OrderLine>(skip, take, orderBy, where: default(object)!).ToList();

            Assert.Equal(50, orderLines.Count);
            Assert.Equal(Enumerable.Range(skip + 1, take), orderLines.Select(ol => ol.OrderLineID));
        }

        [Fact]
        public void C1_AggregationCount()
        {
            var taxRates = connection.QueryAll<OrderLine>()
                .GroupBy(ol => ol.TaxRate)
                .Select(g => new { TaxRate = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToDictionary(x => x.TaxRate, x => x.Count);

            Assert.Equal(2, taxRates.Count);
            Assert.Equal(230376, taxRates[15m]);
            Assert.Equal(1036, taxRates[10m]);
        }

        [Fact]
        public void C2_AggregationMax()
        {
            var maxUnitPrice = connection.MaxAll<OrderLine>(ol => ol.UnitPrice!);

            Assert.Equal(1899m, maxUnitPrice);
        }

        [Fact]
        public void C3_AggregationSum()
        {
            var totalSales = connection.ExecuteQuery<decimal>(
                "SELECT SUM(Quantity * UnitPrice) FROM WideWorldImporters.Sales.OrderLines"
            ).Single();

            Assert.Equal(177634276.4m, totalSales);
        }

        [Fact]
        public void D1_OneToManyRelationship()
        {
            var result = connection.QueryMultiple<Order, OrderLine>(
                o => o.OrderID == 530,
                ol => ol.OrderID == 530
            );

            var order = result.Item1.Single();
            order.OrderLines = result.Item2.ToList();

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

            Assert.Equal(663, customers.Count);
            Assert.Equal(400, customers.Where(c => c.Transactions.Count == 0).Count());
        }

        [Fact]
        public void E1_ColumnSorting()
        {
            var orderBy = OrderField.Ascending<PurchaseOrder>(po => po.ExpectedDeliveryDate!).AsEnumerable();

            var orders = connection.SkipQuery<PurchaseOrder>(0, 1000, orderBy, default(object)!)
                .ToList();

            Assert.Equal(1000, orders.Count);
            Assert.Equal(new DateTime(2013, 1, 15), orders.First().ExpectedDeliveryDate);
            Assert.Equal(new DateTime(2014, 9, 17), orders.Last().ExpectedDeliveryDate);
            Assert.True(orders.SequenceEqual(orders.OrderBy(o => o.ExpectedDeliveryDate)));
        }

        [Fact]
        public void E2_Distinct()
        {
            var supplierReferences = connection.ExecuteQuery<string>(
                "SELECT DISTINCT SupplierReference FROM WideWorldImporters.Purchasing.PurchaseOrders"
            ).ToList();

            Assert.Equal(7, supplierReferences.Count);
            string[] expected = ["AA20384", "BC0280982", "ML0300202", "293092", "08803922", "237408032", "B2084020"];
            Assert.Equal(expected, supplierReferences);
        }

        [Fact]
        public void F1_JSONObjectQuery()
        {
            var sql = """
                SELECT PersonID, FullName, PreferredName, EmailAddress, CustomFields, OtherLanguages
                FROM WideWorldImporters.Application.People
                WHERE JSON_VALUE(CustomFields, '$.Title') = @Title
                ORDER BY PersonID
            """;

            var people = connection.ExecuteQuery<Person>(sql, new { Title = "Team Member" }).ToList();

            Assert.Equal(13, people.Count);
            Assert.All(people, person => Assert.Equal("Team Member", person.CustomFields?.Title));

            var first = people.First();
            Assert.Equal("Kayla Woodcock", first.FullName);
            Assert.Equal("Kayla", first.PreferredName);
            Assert.Equal("kaylaw@wideworldimporters.com", first.EmailAddress);
            Assert.Equal(new DateTime(2008, 4, 19), first.CustomFields?.HireDate);
        }

        [Fact]
        public void F2_JSONArrayQuery()
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

            Assert.Equal(2, people.Count);
            Assert.All(people, person => Assert.Contains("Slovak", person.OtherLanguages!));

            var first = people.First();
            Assert.Equal("Amy Trefl", first.FullName);
            Assert.Equal(["Slovak", "Spanish", "Polish"], first.OtherLanguages);
        }

        [Fact]
        public void G1_Union()
        {
            var suppliers = connection.ExecuteQuery<int>("""
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 5
                    UNION
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 10
                    ORDER BY SupplierID
                """
            ).ToList();

            Assert.Equal(10, suppliers.Count);
            Assert.Equal(Enumerable.Range(1, 10), suppliers);
        }

        [Fact]
        public void G2_Intersection()
        {
            var suppliers = connection.ExecuteQuery<int>("""
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID < 10
                    INTERSECT
                    SELECT SupplierID FROM WideWorldImporters.Purchasing.Suppliers WHERE SupplierID BETWEEN 5 AND 15
                    ORDER BY SupplierID
                """
            ).ToList();

            Assert.Equal([5, 6, 7, 8, 9], suppliers);
        }

        [Fact]
        public void H1_Metadata()
        {
            var datatype = connection.ExecuteQuery<string>("""
                SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS 
                    WHERE TABLE_SCHEMA = 'Purchasing'
                    AND TABLE_NAME = 'Suppliers'
                    AND COLUMN_NAME = 'SupplierReference'
            """
            ).Single();

            Assert.Equal("nvarchar", datatype);
        }
    }
}
