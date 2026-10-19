using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// A path through a reference in an operand of LINQ - <c>o.Customer.Name</c> in a filter, a
/// projection, an ordering, a grouping key - is the shape JPQL and HQL read since decision
/// 144, and decision 148 reads it in the shared LINQ parser by the same mechanism: a path to
/// the key the reference points at is the foreign key column of the holder, without a join;
/// every other reference on the path is a join derived from the relation (decision 101)
/// under an alias of the tool's, one per reference and body. What differs from the Java
/// languages is the kind of that join, which is the provider's rather than the language's:
/// EF Core writes a left join for an optional reference and an inner one for a required
/// reference, and NHibernate's provider a left outer join wherever a NULL could satisfy the
/// clause (decision 151, measured in <c>NHibernate/NHibernateLinqProviderTest</c>) - the same
/// rows - so the shared parser derives whether the row a reference leads to may be absent
/// from the mapping and from the row it is crossed from, and asks the provider's descendant
/// for the kind. The test of a reference for null is the test of its foreign key columns, as
/// decision 101 made it in HQL and JPQL. What the maps do not resolve is refused by name.
/// </summary>
public class LinqReferencePathTest
{
    /// <summary>
    /// Orders referencing customers, customers referencing regions: two owning many-to-one
    /// relations with their inverse sides, the way the resolution phase leaves them. The
    /// foreign key of the order is a scalar property whose language type states its
    /// nullability; <paramref name="nullableForeignKey"/> makes it <c>int?</c>.
    /// <paramref name="withPairs"/> false leaves the relation without columns, as a source
    /// that stated none yields when no catalog was there.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers, EntityMap Regions) Linked(
        bool nullableForeignKey = false,
        bool withPairs = true,
        bool composite = false)
    {
        var regionKey = new Property { Name = "RegionID", Type = LangType.Scalar(ScalarType.Int) };
        var regionName = new Property { Name = "RegionName", Type = LangType.Scalar(ScalarType.String) };
        var regionKeyMap = new PropertyMap { Property = regionKey, ColumnName = "RegionID" };
        var regions = new EntityMap
        {
            Entity = new Entity { Name = "Region", Properties = [regionKey, regionName] },
            Table = "Regions",
            Schema = "Sales",
            PropertyMaps = [regionKeyMap, new PropertyMap { Property = regionName, ColumnName = "RegionName" }],
        };

        var customerKey = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var customerCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var customerName = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var customerYear = new Property { Name = "Year", Type = LangType.Scalar(ScalarType.Int) };
        var customerActive = new Property { Name = "IsActive", Type = LangType.Scalar(ScalarType.Bool) };
        var customerRegion = new Property { Name = "RegionID", Type = LangType.Scalar(ScalarType.Int) };
        var customerKeyMap = new PropertyMap { Property = customerKey, ColumnName = "CustomerID" };
        var customerCompanyMap = new PropertyMap { Property = customerCompany, ColumnName = "CompanyID" };
        var customerRegionMap = new PropertyMap { Property = customerRegion, ColumnName = "RegionID" };
        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerCompany, customerKey, customerName, customerYear, customerActive, customerRegion] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                customerCompanyMap,
                customerKeyMap,
                new PropertyMap { Property = customerName, ColumnName = "CustomerName" },
                new PropertyMap { Property = customerYear, ColumnName = "FoundedYear" },
                new PropertyMap { Property = customerActive, ColumnName = "IsActive" },
                customerRegionMap,
            ],
        };

        var orderCustomer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int, nullableForeignKey) };
        var orderCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int, nullableForeignKey) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var orderCustomerMap = new PropertyMap { Property = orderCustomer, ColumnName = "CustomerID" };
        var orderCompanyMap = new PropertyMap { Property = orderCompany, ColumnName = "CompanyID" };
        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [orderCompany, orderCustomer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [orderCompanyMap, orderCustomerMap, new PropertyMap { Property = total, ColumnName = "Total" }],
        };

        List<ColumnPair> orderPairs = [];
        if (withPairs)
        {
            if (composite)
            {
                orderPairs.Add(new ColumnPair { Source = orderCompanyMap, Target = customerCompanyMap });
            }

            orderPairs.Add(new ColumnPair { Source = orderCustomerMap, Target = customerKeyMap });
        }

        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customer",
            ColumnPairs = orderPairs,
        });
        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.OneToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "Order",
            SourceNavigationProperty = "Orders",
            ColumnPairs = orderPairs,
        });

        List<ColumnPair> regionPairs = [new ColumnPair { Source = customerRegionMap, Target = regionKeyMap }];
        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Customer",
            TargetEntity = "Region",
            SourceNavigationProperty = "Region",
            ColumnPairs = regionPairs,
        });
        regions.Relations.Add(new Relation
        {
            Cardinality = Cardinality.OneToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Region",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customers",
            ColumnPairs = regionPairs,
        });

        return (orders, customers, regions);
    }

    /// <summary>
    /// Orders whose foreign key column no scalar property maps - the usual JPA shape, and the
    /// shape a catalog-completed relation has -, with the navigation property itself carrying
    /// the nullability: <c>Customer?</c>, <c>Customer</c>, or no statement at all.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers) LinkedThroughTheNavigation(bool? nullableNavigation)
    {
        var (_, customers, _) = Linked();
        var customerKeyMap = customers.PropertyMaps.Single(pm => pm.Property.Name == "CustomerID");

        var navigation = new Property
        {
            Name = "Customer",
            Type = nullableNavigation is { } stated ? LangType.Reference("Customer", stated) : null,
        };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var foreignKeyColumn = new PropertyMap { Property = new Property { Name = "CustomerID" }, ColumnName = "CustomerID" };
        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [navigation, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [new PropertyMap { Property = navigation }, new PropertyMap { Property = total, ColumnName = "Total" }],
        };
        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customer",
            ColumnPairs = [new ColumnPair { Source = foreignKeyColumn, Target = customerKeyMap }],
        });

        return (orders, customers);
    }

    private static string Method(string query) =>
        $$"""
        public void Query()
        {
            var q = {{query}}.ToList();
        }
        """;

    private static AbstractQueryBuilder ParseEFCore(string chain, params EntityMap[] maps)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = maps };
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, Method("ctx." + chain), maps);
        return builder;
    }

    private static AbstractQueryBuilder ParseNHibernate(string chain, params EntityMap[] maps)
    {
        var builder = new DapperSqlQueryBuilder { EntityMaps = maps };
        new NHibernateLinqQueryParser(() => builder).Parse(ConversionContentType.CSharp, Method("session." + chain), maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
    {
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
        return artifacts.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
    }

    private static string Failure(AbstractQueryBuilder builder)
    {
        builder.Build();
        return Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure).Reason;
    }

    // ---- the path to the key is the foreign key column ------------------------------------

    [Fact]
    public void APathToTheReferencedKeyIsTheForeignKeyColumnWithoutAJoinUnderBothProviders()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);

        var efCore = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerID == 5)", orders, customers, regions));
        var nhibernate = Sql(ParseNHibernate("Query<Order>().Where(o => o.Customer.CustomerID == 5)", orders, customers, regions));

        // Both providers read the key through the reference as the column the holder carries,
        // so the nullable key brings no join under either, and the texts agree.
        Assert.Contains("WHERE o.CustomerID = 5", efCore);
        Assert.DoesNotContain("JOIN", efCore);
        Assert.Equal(efCore, nhibernate);
    }

    [Fact]
    public void APathToTheKeyNeedsNoMappingOfTheReferencedEntity()
    {
        var (orders, _, _) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerID == 5)", orders));

        Assert.Contains("WHERE o.CustomerID = 5", sql);
    }

    // ---- any other member is a column of the joined entity --------------------------------

    [Fact]
    public void APathToAnotherMemberIsOneJoinSharedByFilterProjectionAndOrdering()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore(
            "Set<Order>().Where(o => o.Customer.CustomerName == \"A\").OrderBy(o => o.Customer.CustomerName).Select(o => new { o.Total, Name = o.Customer.CustomerName })",
            orders, customers, regions));

        // One join for the three paths over the same reference, under the alias the tool
        // derives from the path, with the condition of decision 101; a required reference
        // joins inner in EF Core.
        Assert.Equal(1, sql.Split("JOIN").Length - 1);
        Assert.Contains("INNER JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", sql);
        Assert.Contains("WHERE o_Customer.CustomerName = 'A'", sql);
        Assert.Contains("ORDER BY o_Customer.CustomerName", sql);
        Assert.Contains("o_Customer.CustomerName AS Name", sql);
    }

    [Fact]
    public void ANullableForeignKeyJoinsLeftUnderBothProviders()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);

        var efCore = Sql(ParseEFCore("Set<Order>().Select(o => o.Customer.CustomerName)", orders, customers, regions));
        var nhibernate = Sql(ParseNHibernate("Query<Order>().Select(o => o.Customer.CustomerName)", orders, customers, regions));

        // Both providers keep the order without a customer and project NULL for it: EF Core
        // by the optional relationship, NHibernate 5.7.0 by the left outer join its provider
        // writes for a navigation in a projection (decision 151).
        Assert.Contains("LEFT JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", efCore);
        Assert.Equal(efCore, nhibernate);
    }

    /// <summary>
    /// A reference crossed from a row that may be absent joins left, whatever its own foreign
    /// key says: the inner join a required key would get drops exactly the rows the left join
    /// before it kept (decision 151). The customer's region is required, the order's customer
    /// is not.
    /// </summary>
    [Fact]
    public void AReferenceCrossedFromAnOptionalRowJoinsLeft()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);

        var sql = Sql(ParseEFCore("Set<Order>().Select(o => o.Customer.Region.RegionName)", orders, customers, regions));

        Assert.Contains("LEFT JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", sql);
        Assert.Contains("LEFT JOIN Sales.Regions o_Customer_Region ON o_Customer.RegionID = o_Customer_Region.RegionID", sql);
    }

    /// <summary>The row a left join brings in may be absent too, and so may every row a reference leads to from it.</summary>
    [Fact]
    public void AReferenceCrossedFromTheOptionalSideOfAnOuterJoinJoinsLeft()
    {
        var (orders, customers, regions) = Linked();

        // Both foreign keys are required; the order is on the optional side of the left join.
        var throughTheReference = Sql(ParseEFCore(
            "Set<Customer>().LeftJoin(ctx.Set<Order>(), c => c.CustomerID, o => o.CustomerID, (c, o) => new { c, o }).Select(x => new { x.c.CustomerName, Region = x.o.Customer.Region.RegionName })",
            orders, customers, regions));

        Assert.Contains("LEFT JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", throughTheReference);
        Assert.Contains("LEFT JOIN Sales.Regions o_Customer_Region ON o_Customer.RegionID = o_Customer_Region.RegionID", throughTheReference);
    }

    /// <summary>
    /// A filter through a reference brings a join the chain never composed, and the chain
    /// still materializes its own row: the row of the source is projected whole, so that no
    /// target materializes the pair of the join instead - SQL every column of both tables, LINQ
    /// the tuple, HQL the pair of entities.
    /// </summary>
    [Fact]
    public void AFilterThroughAReferenceStillMaterializesTheSourceRow()
    {
        var (orders, customers, regions) = Linked();
        const string chain = "Set<Order>().Where(o => o.Customer.CustomerName == \"A\")";

        Assert.StartsWith("SELECT o.*", Sql(ParseEFCore(chain, orders, customers, regions)));

        var linq = new EFCoreLinqQueryBuilder { EntityMaps = [orders, customers, regions] };
        new EFCoreLinqQueryParser(() => linq).Parse(ConversionContentType.CSharp, Method("ctx." + chain), [orders, customers, regions]);
        var method = linq.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.Contains(".Select(t => t.o)", method);
        Assert.Contains("IQueryable<Order>", method);

        var hql = new NHibernateHqlQueryBuilder { EntityMaps = [orders, customers, regions] };
        new EFCoreLinqQueryParser(() => hql).Parse(ConversionContentType.CSharp, Method("ctx." + chain), [orders, customers, regions]);
        Assert.StartsWith("select o", hql.Build().Single(s => s.ContentType == ConversionContentType.HqlQuery).Content.TrimStart());
    }

    /// <summary>A path that ends on a reference names a row, not a value: no operand, and no join made for it.</summary>
    [Fact]
    public void APathThatEndsOnAReferenceIsNoOperandAndMakesNoJoin()
    {
        var (orders, customers, regions) = Linked();
        var builder = ParseEFCore("Set<Order>().Select(o => new { o.Total, R = o.Customer.Region })", orders, customers, regions);

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
        Assert.DoesNotContain("JOIN", sql);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("'o.Customer.Region', a navigation", StringComparison.Ordinal));
    }

    /// <summary>
    /// The SQL a path reads into reads back to itself (decision 148): EF Core → Dapper →
    /// Dapper is a fixed point, the join of the path an ordinary join of the text.
    /// </summary>
    [Fact]
    public void TheSqlOfAPathReadsBackToItself()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);
        var sql = Sql(ParseEFCore(
            "Set<Order>().Where(o => o.Customer.CustomerName == \"A\").Select(o => new { o.Total, Name = o.Customer.CustomerName })",
            orders, customers, regions));

        var reread = new DapperSqlQueryBuilder { EntityMaps = [orders, customers, regions] };
        new DapperSqlQueryParser(() => reread).Parse(ConversionContentType.SqlQuery, sql, [orders, customers, regions]);

        Assert.Equal(sql, Sql(reread));
    }

    /// <summary>The null-forgiving mark changes nothing at run time, and the path reads through it.</summary>
    [Fact]
    public void ThePathReadsThroughTheNullForgivingMark()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);

        Assert.Equal(
            Sql(ParseEFCore("Set<Order>().Select(o => o.Customer.Region.RegionName)", orders, customers, regions)),
            Sql(ParseEFCore("Set<Order>().Select(o => o.Customer!.Region!.RegionName)", orders, customers, regions)));
    }

    [Fact]
    public void ARequiredReferenceReadsToTheSameRepresentationUnderBothProviders()
    {
        var (orders, customers, regions) = Linked();

        var efCore = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerName == \"A\").Select(o => o.Total)", orders, customers, regions));
        var nhibernate = Sql(ParseNHibernate("Query<Order>().Where(o => o.Customer.CustomerName == \"A\").Select(o => o.Total)", orders, customers, regions));

        Assert.Contains("INNER JOIN", efCore);
        Assert.Equal(efCore, nhibernate);
    }

    [Theory]
    [InlineData(true, "LEFT JOIN")]
    [InlineData(false, "INNER JOIN")]
    [InlineData(null, "LEFT JOIN")]
    public void TheNullabilityComesFromTheNavigationWhereNoScalarPropertyMapsTheKey(bool? nullableNavigation, string join)
    {
        var (orders, customers) = LinkedThroughTheNavigation(nullableNavigation);

        var sql = Sql(ParseEFCore("Set<Order>().Select(o => o.Customer.CustomerName)", orders, customers));

        // Customer? is optional, Customer required, and a reference nothing types is optional,
        // which is how the EF Core entity builder declares the foreign key property too.
        Assert.Contains($"{join} Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", sql);
    }

    [Fact]
    public void AChainOfReferencesIsOneJoinPerReference()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.Region.RegionName == \"EU\")", orders, customers, regions));

        Assert.Contains("INNER JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", sql);
        Assert.Contains("INNER JOIN Sales.Regions o_Customer_Region ON o_Customer.RegionID = o_Customer_Region.RegionID", sql);
        Assert.Contains("WHERE o_Customer_Region.RegionName = 'EU'", sql);
    }

    [Fact]
    public void AChainThatEndsOnTheKeyOfTheLastReferenceJoinsAllButTheLast()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.Region.RegionID == 3)", orders, customers, regions));

        Assert.Equal(1, sql.Split("JOIN").Length - 1);
        Assert.Contains("WHERE o_Customer.RegionID = 3", sql);
    }

    [Fact]
    public void APathFromAMemberOfAJoinedRowStartsAtThatRow()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore(
            "Set<Customer>().Join(ctx.Set<Order>(), c => c.CustomerID, o => o.CustomerID, (c, o) => new { c, o }).Where(x => x.o.Customer.CustomerName == \"A\").Select(x => x.o.Total)",
            orders, customers, regions));

        Assert.Contains("INNER JOIN Sales.Customers o_Customer ON o.CustomerID = o_Customer.CustomerID", sql);
        Assert.Contains("WHERE o_Customer.CustomerName = 'A'", sql);
    }

    [Fact]
    public void APathInAGroupingKeyAndInAnAggregateOverTheGroupIsRead()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore(
            "Set<Order>().GroupBy(o => o.Customer.CustomerName).Select(g => new { Name = g.Key, Years = g.Max(x => x.Customer.Year) })",
            orders, customers, regions));

        // The element of the group stands for the row of the source, so its path crosses the
        // same reference and shares the join; Year is a column of the customer here, not the
        // function of the vocabulary.
        Assert.Equal(1, sql.Split("JOIN").Length - 1);
        Assert.Contains("GROUP BY o_Customer.CustomerName", sql);
        Assert.Contains("MAX(o_Customer.FoundedYear) AS Years", sql);
    }

    [Fact]
    public void AMemberOfTheVocabularyOverAPathIsTheFunctionOverTheJoinedColumn()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerName.Length > 3)", orders, customers, regions));

        Assert.Contains("INNER JOIN Sales.Customers o_Customer", sql);
        Assert.Contains("(o_Customer.CustomerName) > 3", sql);
    }

    [Fact]
    public void ABooleanMemberThroughAReferenceIsAPredicate()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.IsActive && o.Total > 1)", orders, customers, regions));

        Assert.Contains("WHERE o_Customer.IsActive = 1 AND o.Total > 1", sql);
    }

    // ---- the test of a reference for null -------------------------------------------------

    [Fact]
    public void TheTestOfAReferenceForNullIsTheTestOfItsForeignKeyColumn()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true);

        var efCore = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer != null)", orders, customers, regions));
        var nhibernate = Sql(ParseNHibernate("Query<Order>().Where(o => o.Customer == null)", orders, customers, regions));

        Assert.Contains("WHERE o.CustomerID IS NOT NULL", efCore);
        Assert.DoesNotContain("JOIN", efCore);
        Assert.Contains("WHERE o.CustomerID IS NULL", nhibernate);
    }

    [Fact]
    public void TheTestOfAReferenceOverACompositeKeyTestsEveryColumn()
    {
        var (orders, customers, regions) = Linked(nullableForeignKey: true, composite: true);

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer == null)", orders, customers, regions));

        Assert.Contains("WHERE o.CompanyID IS NULL AND o.CustomerID IS NULL", sql);
    }

    [Fact]
    public void TheTestOfAReferenceBehindAPathCrossesThePath()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Order>().Where(o => o.Customer.Region != null)", orders, customers, regions));

        Assert.Contains("INNER JOIN Sales.Customers o_Customer", sql);
        Assert.Contains("WHERE o_Customer.RegionID IS NOT NULL", sql);
    }

    [Fact]
    public void TheTestOfACollectionOrOfAnInverseReferenceForNullIsRefusedByName()
    {
        var (orders, customers, regions) = Linked();

        var collection = Failure(ParseEFCore("Set<Customer>().Where(c => c.Orders != null)", orders, customers, regions));
        var withoutColumns = Failure(ParseEFCore("Set<Order>().Where(o => o.Customer != null)", Linked(withPairs: false).Orders));

        Assert.Contains("'c.Orders != null' of a collection", collection);
        Assert.Contains("neither the relation states nor the database catalog supplied", withoutColumns);
    }

    // ---- what is refused is refused by name ------------------------------------------------

    [Fact]
    public void AReferencedEntityOutsideTheConversionIsRefusedByName()
    {
        var (orders, _, _) = Linked();

        var reason = Failure(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerName == \"A\")", orders));

        Assert.Contains("'o.Customer' in the path 'o.Customer.CustomerName', which leads to the entity 'Customer', which is not part of the conversion", reason);
    }

    [Fact]
    public void ARelationWithoutColumnsIsRefusedByName()
    {
        var (orders, customers, regions) = Linked(withPairs: false);

        var reason = Failure(ParseEFCore("Set<Order>().Where(o => o.Customer.CustomerName == \"A\")", orders, customers, regions));

        Assert.Contains("states no foreign key columns and the database catalog supplied none", reason);
    }

    [Fact]
    public void APathThroughACollectionIsRefusedByName()
    {
        var (orders, customers, regions) = Linked();

        var reason = Failure(ParseEFCore("Set<Customer>().Where(c => c.Orders.Count > 1)", orders, customers, regions));

        Assert.Contains("'c.Orders' in the path 'c.Orders.Count', which navigates the collection 'Orders' of 'Customer'", reason);
    }

    [Fact]
    public void APathThatWouldJoinInsideTheConditionOfAJoinIsRefusedAndAPathToTheKeyThereIsRead()
    {
        var (orders, customers, regions) = Linked();

        var joining = Failure(ParseEFCore(
            "Set<Customer>().Join(ctx.Set<Order>().Where(o => o.Customer.CustomerName == \"A\"), c => c.CustomerID, o => o.CustomerID, (c, o) => new { c, o })",
            orders, customers, regions));
        var key = Sql(ParseEFCore(
            "Set<Customer>().Join(ctx.Set<Order>().Where(o => o.Customer.CustomerID > 1), c => c.CustomerID, o => o.CustomerID, (c, o) => new { c, o })",
            orders, customers, regions));

        Assert.Contains("would bring an implicit join of 'Customer' inside the condition of a join", joining);
        Assert.Contains("ON c.CustomerID = o.CustomerID AND o.CustomerID > 1", key);
    }

    [Fact]
    public void AReferenceAloneIsStillNoOperand()
    {
        var (orders, customers, regions) = Linked();

        var builder = ParseEFCore("Set<Order>().Select(o => o.Customer)", orders, customers, regions);
        builder.Build();

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("a navigation of the entity rather than a column", StringComparison.Ordinal));
    }

    [Fact]
    public void AMemberThatIsNoReferenceIsReadAsBefore()
    {
        var (orders, customers, regions) = Linked();

        var sql = Sql(ParseEFCore("Set<Customer>().Where(c => c.CustomerName.Length > 3 && c.Region.RegionID == 1)", orders, customers, regions));

        // CustomerName is a column, so Length is the function over it; the path to the key of
        // the region is its foreign key column - no join in the whole query.
        Assert.Contains("(c.CustomerName) > 3 AND c.RegionID = 1", sql);
        Assert.DoesNotContain("JOIN", sql);
    }
}
