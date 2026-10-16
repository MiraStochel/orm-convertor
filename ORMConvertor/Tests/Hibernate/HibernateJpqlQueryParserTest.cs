using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;

namespace Tests.Hibernate;

/// <summary>
/// The JPQL parser of decision 077. The strongest assertion is the round-trip identity:
/// the bare JPQL the builder emits, read back and rebuilt, is the same text - what pins
/// the parser's grammar to the builder's language, as decision 062 did for HQL.
/// </summary>
public class HibernateJpqlQueryParserTest
{
    /// <summary>The column deliberately differs from the property, to prove both name mappings.</summary>
    private static EntityMap Customers()
    {
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };
        var opened = new Property { Name = "OpenedOn", Type = LangType.Scalar(ScalarType.Date) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [name, limit, opened] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimitAmount" },
                new PropertyMap { Property = opened, ColumnName = "OpenedOn" },
            ],
        };
    }

    private static EntityMap Orders()
    {
        var id = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [id, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "CustomerID" },
                new PropertyMap { Property = total, ColumnName = "Total" },
            ],
        };
    }

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string jpql, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, maps);
        return builder;
    }

    private static (string Jpql, IReadOnlyList<ConversionRecord> Records) RoundTrip(string jpql, params EntityMap[] maps)
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), jpql, maps);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return (artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content, builder.Records);
    }

    private static void AssertFixedPoint(string jpql, params EntityMap[] maps)
        => Assert.Equal(jpql, RoundTrip(jpql, maps).Jpql, ignoreLineEndingDifferences: true);

    /* ---- round-trip identity -------------------------------------------------------- */

    [Fact]
    public void ProjectionFilterAndOrderingRoundTrip()
        => AssertFixedPoint("""
            select c.CustomerName as Name
            from Customer c
            where c.CreditLimit > 2000
            order by c.CustomerName desc
            """, Customers());

    [Fact]
    public void AWholeEntityProjectionRoundTrips()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CustomerName like 'A%'
            """, Customers());

    [Fact]
    public void AnEntityJoinRoundTrips()
        => AssertFixedPoint("""
            select o
            from Order o
                join Customer c on o.CustomerID = c.CustomerID
            where c.CreditLimit > 2000
            """, Customers(), Orders());

    [Fact]
    public void ALeftJoinRoundTrips()
        => AssertFixedPoint("""
            select o.Total
            from Order o
                left join Customer c on o.CustomerID = c.CustomerID
            """, Customers(), Orders());

    [Fact]
    public void AggregationGroupingAndHavingRoundTrip()
        => AssertFixedPoint("""
            select o.CustomerID, count(o) as n, sum(o.Total) as total
            from Order o
            group by o.CustomerID
            having count(o) > 5
            """, Orders());

    [Fact]
    public void SubQueriesRoundTrip()
        => AssertFixedPoint("""
            select o
            from Order o
            where o.CustomerID in (select c.CustomerID from Customer c where c.CreditLimit > 2000) and exists (select x from Customer x where x.CustomerID = o.CustomerID)
            """, Customers(), Orders());

    [Fact]
    public void AValueListNullTestsAndNegationRoundTrip()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CreditLimit in (1000, 2500.50) and c.CustomerName is not null and not (c.CustomerName = 'x')
            """, Customers());

    [Fact]
    public void DistinctRoundTrips()
        => AssertFixedPoint("""
            select distinct c.CustomerName
            from Customer c
            """, Customers());

    [Fact]
    public void ATemporalLiteralRoundTripsInTheJdbcEscapeSyntax()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.OpenedOn >= {d '2025-01-01'}
            """, Customers());

    [Fact]
    public void ASetOperationRoundTrips()
        => AssertFixedPoint("""
            select c.CustomerName from Customer c where c.CreditLimit > 2000
            union
            select c.CustomerName from Customer c where c.CreditLimit < 100
            """, Customers());

    [Fact]
    public void MixedAndOrIsParenthesized()
    {
        var (jpql, _) = RoundTrip("""
            select c
            from Customer c
            where (c.CreditLimit > 2000 or c.CreditLimit < 10) and c.CustomerName like 'A%'
            """, Customers());

        Assert.Contains("where (c.CreditLimit > 2000 or c.CreditLimit < 10) and c.CustomerName like 'A%'", jpql);
    }

    /* ---- what the model rewrites --------------------------------------------------- */

    [Fact]
    public void BetweenBecomesTwoComparisons()
    {
        var (jpql, records) = RoundTrip("select c from Customer c where c.CreditLimit between 10 and 20", Customers());

        Assert.Contains("where c.CreditLimit >= 10 and c.CreditLimit <= 20", jpql);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("between"));
    }

    /// <summary>The fourth hook of decision 076: HQL's limit and offset become the pagination of the scope.</summary>
    [Fact]
    public void HqlLimitAndOffsetBecomePagination()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c order by c.CustomerName limit 5 offset 10", Customers());
        var artifacts = builder.Build();

        var method = artifacts.Single(a => a.ContentType == ConversionContentType.JavaQuery).Content;
        Assert.Contains(".setFirstResult(10)", method);
        Assert.Contains(".setMaxResults(5)", method);
        Assert.DoesNotContain("limit", artifacts.Single(a => a.ContentType == ConversionContentType.JpqlQuery).Content);
    }

    [Fact]
    public void AConstructorExpressionKeepsItsColumnsAndDropsTheClass()
    {
        var (jpql, records) = RoundTrip("select new Shop.Dto(c.CustomerName, c.CreditLimit) from Customer c", Customers());

        Assert.StartsWith("select c.CustomerName, c.CreditLimit", jpql);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("Shop.Dto"));
    }

    [Fact]
    public void TheJavaMethodIsReadThroughItsLiteral()
    {
        var builder = new HibernateJpqlQueryBuilder { EntityMaps = [Customers()] };
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.Java, SampleData.CustomerSampleHibernate.Query, [Customers()]);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("where c.CreditLimit > 2000", artifacts.Single(a => a.ContentType == ConversionContentType.JpqlQuery).Content);
    }

    /* ---- refusals (decision 070) ---------------------------------------------------- */

    [Fact]
    public void ASyntaxErrorRefusesWithAPosition()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c where", Customers());

        Assert.Empty(builder.Build());
        var failure = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("line 1, column", failure.Reason);
    }

    /// <summary>
    /// A parameter whose scalar nothing states and nothing implies refuses the artifact
    /// under its own category (decision 083): a method parameter has to have a type, and
    /// guessing one would bind a different value than the source compared.
    /// </summary>
    [Fact]
    public void AParameterWithoutADerivableScalarRefusesUnderItsOwnCategory()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c where :floor > :ceiling", Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.QueryParameter && r.Reason.Contains("floor"));
    }

    /// <summary>
    /// The round-trip identity of this parser holds over a parameter too: JPQL is the one
    /// target language with a positional form, so both spellings come back unchanged
    /// (decision 083).
    /// </summary>
    [Fact]
    public void ANamedParameterRoundTrips()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CreditLimit > :limit
            """, Customers());

    [Fact]
    public void APositionalParameterRoundTrips()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CreditLimit > ?1
            """, Customers());

    [Fact]
    public void ACollectionParameterRoundTrips()
        => AssertFixedPoint("""
            select c
            from Customer c
            where c.CustomerName in :names
            """, Customers());

    /* ---- a join along an association path is derived from the relation (decision 101) ---- */

    /// <summary>
    /// Orders and customers linked by one relation seen from both sides, sharing the
    /// column pairs the way the resolution phase leaves them; without pairs it is what a
    /// source whose columns nobody stated - not the source, not its framework's default,
    /// not the catalog - yields. A collection under mappedBy states no pairs of its own:
    /// the resolution phase leaves them on the owning side alone.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers) Linked(bool withPairs = true, bool inverseStatesPairs = true)
    {
        var customerKey = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var customerKeyMap = new PropertyMap { Property = customerKey, ColumnName = "CustomerID" };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerKey, name] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps = [customerKeyMap, new PropertyMap { Property = name, ColumnName = "CustomerName" }],
        };

        var orderCustomer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var orderCustomerMap = new PropertyMap { Property = orderCustomer, ColumnName = "CustomerID" };

        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Properties = [orderCustomer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [orderCustomerMap, new PropertyMap { Property = total, ColumnName = "Total" }],
        };

        List<ColumnPair> pairs = withPairs ? [new ColumnPair { Source = orderCustomerMap, Target = customerKeyMap }] : [];

        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "customer",
            ColumnPairs = pairs,
        });

        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.OneToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "Order",
            SourceNavigationProperty = "orders",
            ColumnPairs = inverseStatesPairs ? pairs : [],
        });

        return (orders, customers);
    }

    [Fact]
    public void AnOwningAssociationPathJoinDerivesItsConditionFromTheRelation()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer c where c.CustomerName = 'Alice'", orders, customers);

        // Rule Q7: FK(left) = PK(right). The path is the shape JPQL writes a join in; the
        // builder writes back the entity join both implementations read, which returns the
        // same rows (decision 065).
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var jpql = artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Contains("join Customer c on o.CustomerID = c.CustomerID", jpql);
    }

    [Fact]
    public void AnInverseAssociationPathJoinPutsTheForeignKeyOnTheJoinedEntity()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c join c.orders o where o.Total > 100", orders, customers);

        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var jpql = artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Contains("join Order o on o.CustomerID = c.CustomerID", jpql);
    }

    [Fact]
    public void AnInversePathUnderMappedByTakesThePairsOfItsOwningSide()
    {
        var (orders, customers) = Linked(inverseStatesPairs: false);
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c join c.orders o where o.Total > 100", orders, customers);

        // mappedBy names the reference that holds the foreign key; both sides of one
        // relation share its pairs (decision 012), so the collection reads them from there.
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var jpql = artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Contains("join Order o on o.CustomerID = c.CustomerID", jpql);
    }

    [Fact]
    public void AnInversePathBetweenTwoOwningSidesRefusesRatherThanPicksOne()
    {
        var (orders, customers) = Linked(inverseStatesPairs: false);
        var billTo = new PropertyMap { Property = new Property { Name = "BillToID", Type = LangType.Scalar(ScalarType.Int) }, ColumnName = "BillToID" };
        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "billTo",
            ColumnPairs = [new ColumnPair { Source = billTo, Target = customers.PropertyMaps[0] }],
        });
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c join c.orders o", orders, customers);

        // Which of two foreign keys the collection follows is a claim nobody made here.
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("no foreign key columns"));
    }

    [Fact]
    public void AWrittenOnConditionJoinsTheDerivedConjunction()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer c on c.CustomerName = 'Alice'", orders, customers);

        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var jpql = artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
        Assert.Contains("join Customer c on (o.CustomerID = c.CustomerID and c.CustomerName = 'Alice')", jpql);
    }

    [Fact]
    public void AnAssociationPathWithoutARelationRefusesByName()
    {
        // The fixtures of this class declare no relation, so the path names nothing.
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer c where c.CreditLimit > 1", Orders(), Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("o.customer") && r.Reason.Contains("names no association"));
    }

    [Fact]
    public void AnAssociationPathWithoutResolvedColumnsRefusesRatherThanGuesses()
    {
        var (orders, customers) = Linked(withPairs: false);
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer c", orders, customers);

        // The JPA default join column is the entity parser's to state (decision 067), not
        // the query reader's to guess.
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("no foreign key columns"));
    }

    /* ---- a path through an association outside join (decision 144) ---------------- */

    /// <summary>Orders, customers and regions: the customer refers to its region through an owning reference with stated pairs.</summary>
    private static (EntityMap Orders, EntityMap Customers, EntityMap Regions) LinkedThroughRegion()
    {
        var (orders, customers) = Linked();

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

        var customerRegionMap = new PropertyMap { Property = new Property { Name = "RegionID", Type = LangType.Scalar(ScalarType.Int) }, ColumnName = "RegionID" };
        customers.PropertyMaps.Add(customerRegionMap);
        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Customer",
            TargetEntity = "Region",
            SourceNavigationProperty = "region",
            ColumnPairs = [new ColumnPair { Source = customerRegionMap, Target = regionKeyMap }],
        });

        return (orders, customers, regions);
    }

    [Fact]
    public void APathToTheReferencedKeyReadsTheForeignKeyColumnWithoutAJoin()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o where o.customer.CustomerID = 7", orders, customers);

        // The key the owning reference points at is the foreign key the order carries: the
        // reading Hibernate makes of the path, with no join of the customer.
        var jpql = Build(builder);

        Assert.Contains("where o.CustomerID = 7", jpql);
        Assert.DoesNotContain("join", jpql);
    }

    [Fact]
    public void APathToTheReferencedKeyWithoutAScalarPropertyIsAFixedPoint()
    {
        var (orders, customers) = Linked();
        orders.PropertyMaps.RemoveAll(p => p.Property.Name == "CustomerID");

        // The column the reference alone maps is written back as the path through the
        // reference (ColumnMember), so the language's own spelling reads back to itself.
        AssertFixedPoint("""
            select o
            from Order o
            where o.customer.CustomerID = :customerId
            """, orders, customers);
    }

    [Fact]
    public void APathThroughAReferenceBringsOneImplicitJoinForEveryPathOverIt()
    {
        var (orders, customers) = Linked();
        var builder = Parse(
            new HibernateJpqlQueryBuilder(),
            "select o.customer.CustomerName from Order o where o.customer.CustomerName like 'A%' order by o.customer.CustomerName",
            orders,
            customers);

        // Rule Q7 outside join: the path is an inner join of the customer with the derived
        // condition, made once for the association and shared by the projection, the filter
        // and the ordering.
        var jpql = Build(builder);

        Assert.Contains("join Customer o_customer on o.CustomerID = o_customer.CustomerID", jpql);
        Assert.Equal(1, jpql.Split("join ").Length - 1);
        Assert.Contains("select o_customer.CustomerName", jpql);
        Assert.Contains("where o_customer.CustomerName like 'A%'", jpql);
        Assert.Contains("order by o_customer.CustomerName", jpql);
    }

    [Fact]
    public void APathThroughTwoReferencesCrossesTwoImplicitJoins()
    {
        var (orders, customers, regions) = LinkedThroughRegion();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o where o.customer.region.RegionName = 'EU'", orders, customers, regions);

        var jpql = Build(builder);

        Assert.Contains("join Customer o_customer on o.CustomerID = o_customer.CustomerID", jpql);
        Assert.Contains("join Region o_customer_region on o_customer.RegionID = o_customer_region.RegionID", jpql);
        Assert.Contains("where o_customer_region.RegionName = 'EU'", jpql);
    }

    [Fact]
    public void AJoinAlongAPathOfTwoAssociationsCrossesTheFirstAsAnImplicitJoin()
    {
        var (orders, customers, regions) = LinkedThroughRegion();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join o.customer.region r where r.RegionName = 'EU'", orders, customers, regions);

        var jpql = Build(builder);

        Assert.Contains("join Customer o_customer on o.CustomerID = o_customer.CustomerID", jpql);
        Assert.Contains("join Region r on o_customer.RegionID = r.RegionID", jpql);
    }

    [Fact]
    public void APathInsideAJoinConditionReadsTheForeignKeyButBringsNoJoin()
    {
        var (orders, customers) = Linked();
        var keyed = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join Customer c on c.CustomerID = o.customer.CustomerID", orders, customers);
        var joined = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o join Customer c on c.CustomerName = o.customer.CustomerName", orders, customers);

        // The foreign key needs no join and reads inside the condition; a path that would
        // bring a join cannot stand there, because the join it brings would have to precede
        // the join whose condition names it.
        Assert.Contains("join Customer c on c.CustomerID = o.CustomerID", Build(keyed));
        Assert.Empty(joined.Build());
        Assert.Contains(joined.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("inside the condition of a join"));
    }

    [Fact]
    public void APathThroughACollectionRefusesByName()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select c from Customer c where c.orders.Total > 100", orders, customers);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("navigates the collection 'orders'"));
    }

    [Theory]
    [InlineData("select o from Order o where o.customer.CustomerName = 'A'", false, true, "the mapping of the entity behind 'o'")]
    [InlineData("select o from Order o where o.shipper.Name = 'A'", true, true, "names no association")]
    [InlineData("select o from Order o where o.customer.CustomerName = 'A'", true, false, "states no foreign key columns")]
    [InlineData("select o from Order o where x.customer.CustomerName = 'A'", true, true, "no identification variable")]
    public void APathTheMapsDoNotResolveRefusesByName(string jpql, bool withMaps, bool withPairs, string reason)
    {
        var (orders, customers) = Linked(withPairs: withPairs);
        var builder = withMaps
            ? Parse(new HibernateJpqlQueryBuilder(), jpql, orders, customers)
            : Parse(new HibernateJpqlQueryBuilder(), jpql);

        // Nothing is guessed (decision 067); the filter refuses the artifact and names the
        // path and what is missing.
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains(reason));
    }

    [Fact]
    public void AnImplicitJoinReachesSqlAsAnOrdinaryJoin()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new DapperSqlQueryBuilder(), "select o from Order o where o.customer.CustomerName = 'Alice'", orders, customers);

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("INNER JOIN Sales.Customers o_customer ON o.CustomerID = o_customer.CustomerID", sql);
        Assert.Contains("o_customer.CustomerName = 'Alice'", sql);
    }

    private static string Build(AbstractQueryBuilder builder)
    {
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts.Single(s => s.ContentType == ConversionContentType.JpqlQuery).Content;
    }

    [Fact]
    public void ACrossJoinRefuses()
    {
        var builder = Parse(new HibernateJpqlQueryBuilder(), "select o from Order o, Customer c", Orders(), Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join);
    }

    [Fact]
    public void AQueryComposedAtRunTimeIsAnIncompleteness()
    {
        var builder = new HibernateJpqlQueryBuilder();
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.Java, """
            public static Query query(EntityManager em, String where) {
                return em.createQuery("select c from Customer c " + where);
            }
            """);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Reason.Contains("composed at run time"));
    }

    /// <summary>
    /// Bare JPQL, Java - a whole file or a fragment of one, whatever it holds besides
    /// (decision 111) - and XML, for the named queries of an orm.xml (decision 143). The
    /// value of the query artifact is the builder's, not a language a unit declares.
    /// </summary>
    [Fact]
    public void TheParserClaimsJpqlJavaAndXml()
    {
        var parser = new HibernateJpqlQueryParser(() => new HibernateJpqlQueryBuilder());

        Assert.True(parser.CanParse(ConversionContentType.JpqlQuery));
        Assert.True(parser.CanParse(ConversionContentType.Java));

        // The orm.xml carries named queries, so the document is read by both passes (decisions 081 and 143).
        Assert.True(parser.CanParse(ConversionContentType.XML));
        Assert.False(parser.CanParse(ConversionContentType.JavaQuery));
        Assert.False(parser.CanParse(ConversionContentType.HqlQuery));
        Assert.False(parser.CanParse(ConversionContentType.CSharp));
    }

    /// <summary>
    /// The instruction list the parser produces, checked once directly: names go through
    /// the mapping IR to the column the query builder of another target needs.
    /// </summary>
    [Fact]
    public void NamesGoThroughTheMappingToColumns()
    {
        var builder = new DummyQueryBuilder { EntityMaps = [Customers()] };
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, "select c.CreditLimit from Customer c where c.CreditLimit > 1", [Customers()]);

        var body = ((SubQueryInstruction)builder.Instructions.Single()).Instructions;
        var from = Assert.Single(body.OfType<FromInstruction>());
        Assert.Equal("Sales.Customers", from.Table);
        Assert.Equal("CreditLimitAmount", Assert.Single(body.OfType<ProjectInstruction>()).Operand.Property);
    }

    private sealed class DummyQueryBuilder : AbstractQueryBuilder
    {
        public override TargetFrameworkDescriptor Descriptor => HibernateDescriptor.Instance;

        public IReadOnlyList<QueryInstruction> Instructions => instructions;

        protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact) { }
        protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact) { }
        protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact) => [];
    }
}
