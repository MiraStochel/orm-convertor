using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.NHibernate;

/// <summary>
/// The HQL parser of decision 062. The strongest assertion here is the round-trip identity:
/// the bare HQL the builder emits, read back and rebuilt, must be the same text - that is
/// what pins the parser's grammar to the builder's language and what T3 compares for the
/// NHibernate → NHibernate direction.
/// </summary>
public class NHibernateHqlQueryParserTest
{
    /// <summary>The column deliberately differs from the property, to prove both name mappings.</summary>
    private static EntityMap Customers()
    {
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var limit = new Property { Name = "CreditLimit", Type = LangType.Scalar(ScalarType.Decimal) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [name, limit] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = name, ColumnName = "CustomerName" },
                new PropertyMap { Property = limit, ColumnName = "CreditLimitAmount" },
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

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string hql, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, maps);
        return builder;
    }

    private static string RoundTrip(string hql, params EntityMap[] maps)
    {
        var builder = Parse(new NHibernateHqlQueryBuilder(), hql, maps);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;
    }

    /* ---- round-trip identity -------------------------------------------------------- */

    [Fact]
    public void ProjectionFilterAndOrderingRoundTripToTheSameText()
    {
        const string hql = """
            select c.CustomerName as Name
            from Customer c
            where c.CreditLimit > 2000
            order by c.CustomerName desc
            """;

        Assert.Equal(hql, RoundTrip(hql, Customers()), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void AJoinRoundTripsToTheSameText()
    {
        const string hql = """
            from Order o
                inner join Customer c with o.CustomerID = c.CustomerID
            where c.CreditLimit > 2000
            """;

        Assert.Equal(hql, RoundTrip(hql, Customers(), Orders()), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void AggregationGroupingAndHavingRoundTripToTheSameText()
    {
        const string hql = """
            select o.CustomerID, count(*) as n
            from Order o
            group by o.CustomerID
            having count(*) > 5
            """;

        Assert.Equal(hql, RoundTrip(hql, Orders()), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void SubQueriesRoundTripToTheSameText()
    {
        const string hql = """
            from Order o
            where o.CustomerID in (select c.CustomerID from Customer c where c.CreditLimit > 2000) and exists (from Customer x where x.CustomerID = o.CustomerID)
            """;

        var customers = Customers();
        customers.PropertyMaps.Add(new PropertyMap
        {
            Property = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) },
            ColumnName = "CustomerID",
        });

        Assert.Equal(hql, RoundTrip(hql, customers, Orders()), ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// The literal forms the builder writes come back out unchanged: the escaped quote, the
    /// lowercase boolean, the bare negative number.
    /// </summary>
    [Fact]
    public void LiteralsRoundTripToTheSameText()
    {
        const string hql = """
            from Customer c
            where c.CustomerName like 'O''Brien%' and not (c.CreditLimit = -5)
            """;

        Assert.Equal(hql, RoundTrip(hql, Customers()), ignoreLineEndingDifferences: true);
    }

    /* ---- names go through the mapping IR -------------------------------------------- */

    /// <summary>
    /// The parser is the inverse of the builder's visitor: the property CreditLimit maps to
    /// the column CreditLimitAmount on the way in, so a SQL target sees the column - the
    /// same query the LINQ source could only hand over by property name.
    /// </summary>
    [Fact]
    public void PropertyNamesBecomeColumnsForASqlTarget()
    {
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            "from Customer c where c.CreditLimit > 2000",
            Customers());

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("FROM Sales.Customers AS c", sql);
        Assert.Contains("c.CreditLimitAmount > 2000", sql);
    }

    [Fact]
    public void AFullJoinIsCarriedAsFullForATargetThatHasIt()
    {
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            "from Order o full join Customer c with o.CustomerID = c.CustomerID",
            Customers(),
            Orders());

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("FULL JOIN", sql);
    }

    /* ---- what the model cannot carry is a record, never a guess --------------------- */

    /* ---- a join along an association path is derived from the relation (decision 101) ---- */

    /// <summary>
    /// Orders and customers linked by one relation seen from both sides: the owning
    /// many-to-one from the order and the inverse one-to-many from the customer share the
    /// column pairs, the way the resolution phase leaves them. Without pairs the relation
    /// is what a source that stated no columns yields when no catalog was there. An inverse
    /// side may state no pairs of its own - an inverse one-to-one under property-ref admits
    /// no column - and the resolution phase leaves them on the owning side alone then.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers) Linked(bool withPairs = true, bool composite = false, bool inverseStatesPairs = true)
    {
        var customerKey = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var customerCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var customerKeyMap = new PropertyMap { Property = customerKey, ColumnName = "CustomerID" };
        var customerCompanyMap = new PropertyMap { Property = customerCompany, ColumnName = "CompanyID" };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Namespace = "Shop", Properties = [customerCompany, customerKey, name] },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps = [customerCompanyMap, customerKeyMap, new PropertyMap { Property = name, ColumnName = "CustomerName" }],
        };

        var orderCustomer = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var orderCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var orderCustomerMap = new PropertyMap { Property = orderCustomer, ColumnName = "CustomerID" };
        var orderCompanyMap = new PropertyMap { Property = orderCompany, ColumnName = "CompanyID" };

        var orders = new EntityMap
        {
            Entity = new Entity { Name = "Order", Namespace = "Shop", Properties = [orderCompany, orderCustomer, total] },
            Table = "Orders",
            Schema = "Sales",
            PropertyMaps = [orderCompanyMap, orderCustomerMap, new PropertyMap { Property = total, ColumnName = "Total" }],
        };

        List<ColumnPair> pairs = [];
        if (withPairs)
        {
            if (composite)
            {
                pairs.Add(new ColumnPair { Source = orderCompanyMap, Target = customerCompanyMap });
            }

            pairs.Add(new ColumnPair { Source = orderCustomerMap, Target = customerKeyMap });
        }

        orders.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToOne,
            Role = RelationRole.Owning,
            SourceEntity = "Order",
            TargetEntity = "Customer",
            SourceNavigationProperty = "Customer",
            ColumnPairs = pairs,
        });

        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.OneToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "Order",
            SourceNavigationProperty = "Orders",
            ColumnPairs = inverseStatesPairs ? pairs : [],
        });

        return (orders, customers);
    }

    private static string Hql(AbstractQueryBuilder builder)
    {
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;
    }

    [Fact]
    public void AnOwningAssociationPathJoinDerivesItsConditionFromTheRelation()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Order o join o.Customer c where c.CustomerName = 'Alice'", orders, customers);

        // Rule Q7: FK(left) = PK(right), with the foreign key on the entity behind the path
        // because the relation is the owning one; the builder writes it as the entity join
        // it always writes, which returns the same rows (decision 065).
        var hql = Hql(builder);

        Assert.Contains("inner join Customer c with o.CustomerID = c.CustomerID", hql);
        Assert.Contains("where c.CustomerName = 'Alice'", hql);
    }

    [Fact]
    public void AnInverseAssociationPathJoinPutsTheForeignKeyOnTheJoinedEntity()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Customer c left join c.Orders o where o.Total > 100", orders, customers);

        var hql = Hql(builder);

        // Order is a keyword of HQL, which NHibernate reads as the target of an entity join
        // only qualified with its namespace (HqlNames).
        Assert.Contains("left join Shop.Order o with o.CustomerID = c.CustomerID", hql);
    }

    [Fact]
    public void AnInversePathWithoutPairsOfItsOwnTakesThoseOfItsOwningSide()
    {
        var (orders, customers) = Linked(inverseStatesPairs: false);
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Customer c join c.Orders o", orders, customers);

        // Both sides of one relation share its pairs (decision 012); the JPQL and LINQ
        // parsers read them from the owning side the same way.
        var hql = Hql(builder);

        Assert.Contains("inner join Shop.Order o with o.CustomerID = c.CustomerID", hql);
    }

    [Fact]
    public void ACompositeKeyPathJoinsOverEveryPairInTheirOrder()
    {
        var (orders, customers) = Linked(composite: true);
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Order o join o.Customer c", orders, customers);

        var hql = Hql(builder);

        Assert.Matches(@"o\.CompanyID = c\.CompanyID and o\.CustomerID = c\.CustomerID", hql);
    }

    /* ---- an entity tested for null ------------------------------------------------- */

    /// <summary>
    /// <c>o.Customer is null</c> tests the reference, not a column: read as one, it came out as
    /// <c>o.Customer IS NULL</c>, a column no table has. An owning reference is the nullness of
    /// its foreign key columns, every one of them over a composite key (decision 101).
    /// </summary>
    [Theory]
    [InlineData("from Order o where o.Customer is null", false, "WHERE o.CustomerID IS NULL")]
    [InlineData("from Order o where o.Customer is not null", false, "WHERE o.CustomerID IS NOT NULL")]
    [InlineData("from Order o where o.Customer is null", true, "WHERE o.CompanyID IS NULL AND o.CustomerID IS NULL")]
    public void AReferenceTestedForNullIsItsForeignKeyTestedForNull(string hql, bool composite, string expected)
    {
        var (orders, customers) = Linked(composite: composite);
        var builder = Parse(new DapperSqlQueryBuilder(), hql, orders, customers);

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains(expected, sql);
        Assert.DoesNotContain("Customer IS", sql);
    }

    /// <summary>A collection, a declared alias alone and a reference whose columns nobody states have no column to test; each is refused by name.</summary>
    [Theory]
    [InlineData("from Customer c where c.Orders is null", true, "collection")]
    [InlineData("from Order o where o is not null", true, "whole row")]
    [InlineData("from Order o where o.Customer is null", false, "neither the relation states")]
    public void AnyOtherEntityTestedForNullIsRefused(string hql, bool withPairs, string reason)
    {
        var (orders, customers) = Linked(withPairs: withPairs);
        var builder = Parse(new DapperSqlQueryBuilder(), hql, orders, customers);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains(reason, StringComparison.Ordinal));
    }

    /* ---- a reference compared with the row it points at ------------------------------ */

    /// <summary>
    /// <c>o.Customer = c</c> says the reference points at the row, the same as the join along
    /// the association, and is read as the equality of the foreign key columns with the key,
    /// derived from the relation (decision 101); <c>&lt;&gt;</c> is its negation. Read as a
    /// column compared with the entity, it came out as <c>c.*</c> in every target.
    /// </summary>
    [Theory]
    [InlineData("from Order o join Customer c with o.Customer = c", false, ORMEnum.Dapper, "ON o.CustomerID = c.CustomerID")]
    [InlineData("from Order o join Customer c with c = o.Customer", false, ORMEnum.Dapper, "ON o.CustomerID = c.CustomerID")]
    [InlineData("from Order o join o.Customer c where o.Customer <> c", false, ORMEnum.Dapper, "WHERE NOT (o.CustomerID = c.CustomerID)")]
    [InlineData("from Order o join Customer c with o.Customer = c", true, ORMEnum.Dapper, "ON o.CompanyID = c.CompanyID AND o.CustomerID = c.CustomerID")]
    [InlineData("from Order o join Customer c with o.Customer = c", false, ORMEnum.NHibernate, "inner join Customer c with o.CustomerID = c.CustomerID")]
    [InlineData("from Order o join Customer c with o.Customer = c", false, ORMEnum.EFCore, "o => o.CustomerID, c => c.CustomerID")]
    public void AReferenceComparedWithTheRowIsTheEqualityOfItsKey(string hql, bool composite, ORMEnum target, string expected)
    {
        var (orders, customers) = Linked(composite: composite);
        AbstractQueryBuilder builder = target switch
        {
            ORMEnum.Dapper => new DapperSqlQueryBuilder(),
            ORMEnum.NHibernate => new NHibernateHqlQueryBuilder(),
            _ => new EFCoreLinqQueryBuilder(),
        };
        builder = Parse(builder, hql, orders, customers);

        var artifacts = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var written = string.Join("\n", artifacts.Select(a => a.Content));

        Assert.Contains(expected, written);
        Assert.DoesNotContain(".*", written);
    }

    /// <summary>
    /// An entity compared with anything else - a parameter, a value, another declared alias,
    /// a collection, or a reference whose columns nobody states - has no operand in the
    /// representation, and the condition is refused by name, not written as a column.
    /// </summary>
    [Theory]
    [InlineData("from Order o where o.Customer = :customer", true)]
    [InlineData("from Order o where o.Customer = 1", true)]
    [InlineData("from Order o join Customer c with o = c", true)]
    [InlineData("from Customer c join Order o with c.Orders = o", true)]
    [InlineData("from Order o join Customer c with o.Customer = c", false)]
    public void AnyOtherComparisonOfAnEntityIsRefused(string hql, bool withPairs)
    {
        var (orders, customers) = Linked(withPairs: withPairs);
        var builder = Parse(new DapperSqlQueryBuilder(), hql, orders, customers);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("whole entity", StringComparison.Ordinal));
    }

    [Fact]
    public void AWrittenWithConditionJoinsTheDerivedConjunction()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Order o join o.Customer c with c.CustomerName = 'Alice'", orders, customers);

        var hql = Hql(builder);

        Assert.Matches(@"o\.CustomerID = c\.CustomerID and c\.CustomerName = 'Alice'", hql);
    }

    [Fact]
    public void AnAssociationPathJoinWithoutAnAliasTakesTheAttributeName()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Order o join fetch o.Customer", orders, customers);

        var hql = Hql(builder);

        Assert.Contains("with o.CustomerID = Customer.CustomerID", hql);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("fetch"));
    }

    [Fact]
    public void AnAssociationPathJoinReachesSqlAsAnOrdinaryJoin()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new DapperSqlQueryBuilder(), "from Order o join o.Customer c where c.CustomerName = 'Alice'", orders, customers);

        var sql = builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.Contains("INNER JOIN Sales.Customers c ON o.CustomerID = c.CustomerID", sql);
    }

    [Theory]
    [InlineData("from Order o join o.Customer c", false, "the mapping of the entity behind 'o'")]
    [InlineData("from Order o join o.Shipper s", true, "names no association")]
    [InlineData("from Order o join o.Customer.Region r", true, "crosses more than one association")]
    public void AnAssociationPathTheMapsDoNotResolveRefusesByName(string hql, bool withMaps, string reason)
    {
        var (orders, customers) = Linked();
        var builder = withMaps
            ? Parse(new NHibernateHqlQueryBuilder(), hql, orders, customers)
            : Parse(new NHibernateHqlQueryBuilder(), hql);

        // Nothing is guessed (decision 067) and a query without its join would return
        // different rows (decision 070): no artifact, and the record names the path and
        // what is missing.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("o.") && r.Reason.Contains(reason));
    }

    [Fact]
    public void AnAssociationPathWithoutResolvedColumnsRefusesRatherThanGuesses()
    {
        var (orders, customers) = Linked(withPairs: false);
        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Order o join o.Customer c", orders, customers);

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("no foreign key columns"));
    }

    [Fact]
    public void AnAssociationPathAcrossAManyToManyRefuses()
    {
        var (orders, customers) = Linked();
        customers.Relations.Add(new Relation
        {
            Cardinality = Cardinality.ManyToMany,
            Role = RelationRole.Inverse,
            SourceEntity = "Customer",
            TargetEntity = "Order",
            SourceNavigationProperty = "Favourites",
        });

        var builder = Parse(new NHibernateHqlQueryBuilder(), "from Customer c join c.Favourites f", orders, customers);

        // Two joins over the junction entity of decision 005 and an alias nobody wrote: a
        // stated limit of decision 101, refused by name.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("many-to-many"));
    }

    [Fact]
    public void AnInWithAValueListRoundTrips()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CustomerName in ('Alice', 'Bob')",
            Customers());

        // The list is the fourth operand shape (decision 074): read as typed constants and
        // written back through the same literal rendering a lone constant gets.
        var hql = builder.Build().Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;

        Assert.Contains("c.CustomerName in ('Alice', 'Bob')", hql);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AnInWithANullAmongItsValuesRefusesTheArtifact()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CustomerName not in ('Alice', null)",
            Customers());

        // Null is no value the model carries (decision 002), and a not in over it means
        // different things in HQL and in LINQ; the record names it (decision 074).
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure
                 && r.Feature == QueryFeature.Filtering
                 && r.Reason.Contains("null among the values"));
    }

    /// <summary>
    /// A named parameter comes back as itself (decision 083): the colon is HQL's decoration,
    /// stripped on the way in and added on the way out, and the generated method carries the
    /// value typed from the column the parameter is compared against.
    /// </summary>
    [Fact]
    public void ANamedParameterRoundTripsAndTypesTheMethod()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CreditLimit > :limit",
            Customers());

        var outputs = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains(
            "where c.CreditLimit > :limit",
            outputs.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content);

        var method = outputs.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.Contains("(ISession session, decimal limit)", method);
        Assert.Contains(".SetParameter(\"limit\", limit)", method);
    }

    /// <summary>
    /// The scalar comes from the mapping IR, so a parameter with nothing typed on the other
    /// side of the comparison refuses the artifact under its own category (decision 083).
    /// </summary>
    [Fact]
    public void AParameterWithoutADerivableScalarRefusesTheArtifact()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where :floor > :ceiling",
            Customers());

        // Both parameters are reported: reading goes on so that every reason reaches the
        // caller at once, which is how this parser has always answered.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure
                 && r.Feature == QueryFeature.QueryParameter
                 && r.Reason.Contains("floor"));
    }

    /// <summary>
    /// DISTINCT is a flag of the (sub)query scope written by the projection step
    /// (decision 073); it used to refuse the artifact here. Over the whole entity the select
    /// clause names the alias, the one case a select is written without a projection.
    /// </summary>
    [Theory]
    [InlineData("select distinct c.CustomerName\nfrom Customer c")]
    [InlineData("select distinct c\nfrom Customer c")]
    public void SelectDistinctRoundTripsToTheSameText(string hql)
        => Assert.Equal(hql, RoundTrip(hql, Customers()), ignoreLineEndingDifferences: true);

    [Fact]
    public void ABetweenIsRewrittenAsAPairOfComparisons()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CreditLimit between 100 and 200",
            Customers());

        var hql = builder.Build().Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;

        Assert.Contains("c.CreditLimit >= 100 and c.CreditLimit <= 200", hql);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
    }

    /* ---- syntax errors refuse with a position --------------------------------------- */

    [Fact]
    public void ASyntaxErrorRefusesTheArtifactWithLineAndColumn()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c\nwhere c.CreditLimit > ",
            Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("line 2"));
    }

    /// <summary>HQL in NHibernate 5.7.0 has no set operations, so a union is not valid input.</summary>
    [Fact]
    public void AUnionIsASyntaxError()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c union from Customer d",
            Customers());

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("'union'"));
    }
}

/// <summary>
/// The direction through the real orchestration: a bare HQL unit declared as HqlQuery is
/// read by the HQL parser, and NHibernate → NHibernate returns the same text - the round
/// trip that closed exempt area 4 of the guarantees boundary.
/// </summary>
public class NHibernateHqlRoundTripTest
{
    private const string Entity = """
        namespace Shop;

        public class Customer
        {
            public virtual int CustomerID { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }
        """;

    private const string Mapping = """
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop" assembly="Shop">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerID" column="CustomerID" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerName" type="String" />
            <property name="CreditLimit" column="CreditLimit" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

    private const string Hql = """
        from Customer c
        where c.CreditLimit > 2000
        order by c.CustomerName asc
        """;

    [Fact]
    public void NHibernateToNHibernateIsATextualRoundTrip()
    {
        List<ConversionSource> sources =
        [
            new() { Content = Entity, ContentType = ConversionContentType.CSharp },
            new() { Content = Mapping, ContentType = ConversionContentType.XML },
            new() { Content = Hql, ContentType = ConversionContentType.HqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.NHibernate, sources);

        var hql = result.Sources.Single(s => s.ContentType == ConversionContentType.HqlQuery).Content;

        Assert.Equal(Hql, hql, ignoreLineEndingDifferences: true);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>An HQL unit reaches every target, not only NHibernate itself.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery, "SELECT")]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "ctx.Set<Customer>()")]
    public void AnHqlUnitTranslatesToTheOtherTargets(
        ORMEnum target,
        ConversionContentType artifactType,
        string hallmark)
    {
        List<ConversionSource> sources =
        [
            new() { Content = Entity, ContentType = ConversionContentType.CSharp },
            new() { Content = Mapping, ContentType = ConversionContentType.XML },
            new() { Content = Hql, ContentType = ConversionContentType.HqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, target, sources);

        var query = result.Sources.First(s => s.ContentType == artifactType).Content;

        Assert.Contains(hallmark, query);
    }
}
