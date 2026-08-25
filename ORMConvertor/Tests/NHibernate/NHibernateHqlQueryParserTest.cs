using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
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
        new NHibernateHqlQueryParser(builder).Parse(ConversionContentType.HqlQuery, hql, maps);
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

    [Fact]
    public void AnAssociationPathJoinIsDroppedWithARecord()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Order o join o.Customer c where o.Total > 100",
            Orders());

        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("association path"));
    }

    [Fact]
    public void AnInWithAValueListDropsTheFilterWithARecord()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CustomerName in ('Alice', 'Bob')",
            Customers());

        // The same road the Dapper parser takes: the artifact comes out without the filter
        // and the clause says so.
        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("where clause"));
    }

    [Fact]
    public void AQueryParameterDropsTheFilterWithARecord()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "from Customer c where c.CreditLimit > :limit",
            Customers());

        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("where clause"));
    }

    [Fact]
    public void SelectDistinctIsALoss()
    {
        var builder = Parse(
            new NHibernateHqlQueryBuilder(),
            "select distinct c.CustomerName from Customer c",
            Customers());

        Assert.NotEmpty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("distinct"));
    }

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
            new() { Content = Entity, ContentType = ConversionContentType.CSharpEntity },
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
            new() { Content = Entity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = Mapping, ContentType = ConversionContentType.XML },
            new() { Content = Hql, ContentType = ConversionContentType.HqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, target, sources);

        var query = result.Sources.First(s => s.ContentType == artifactType).Content;

        Assert.Contains(hallmark, query);
    }
}
