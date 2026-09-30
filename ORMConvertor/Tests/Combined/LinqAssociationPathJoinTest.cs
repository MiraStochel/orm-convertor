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
/// A second <c>from</c> over a collection of the row - <c>from c in ctx.Customers from o in
/// c.Orders</c>, which is <c>SelectMany(c =&gt; c.Orders, (c, o) =&gt; …)</c> - is the join
/// along an association path in the shape LINQ writes it. Decision 101 derives that join
/// from the relation of the mapping (paper rule Q7) and read it in HQL and JPQL; until
/// 2026-09-30 the shared LINQ parser refused every SelectMany as a join it cannot carry
/// (decision 070), whatever its collection was. Now the navigation is matched to the
/// relation of the entity behind the row, the condition comes from its column pairs, and
/// the join reaches the builder in the shape a Join with key selectors takes - so what the
/// join tests prove holds for the path too. What the maps do not hold is refused by the
/// same four sentences the HQL and JPQL parsers say, and a SelectMany over a second source
/// stays refused as the cross join it is.
/// </summary>
public class LinqAssociationPathJoinTest
{
    /// <summary>
    /// Customers and orders linked by one relation seen from both sides, the way the
    /// resolution phase leaves them: the owning many-to-one from the order and the inverse
    /// one-to-many from the customer share the column pairs. In LINQ only the collection
    /// side is navigated by a second <c>from</c>, so the inverse one is the path read here.
    /// Without pairs the relation is what a source that stated no columns yields when no
    /// catalog was there.
    /// </summary>
    private static (EntityMap Orders, EntityMap Customers) Linked(bool withPairs = true, bool composite = false)
    {
        var customerKey = new Property { Name = "CustomerID", Type = LangType.Scalar(ScalarType.Int) };
        var customerCompany = new Property { Name = "CompanyID", Type = LangType.Scalar(ScalarType.Int) };
        var name = new Property { Name = "CustomerName", Type = LangType.Scalar(ScalarType.String) };
        var customerKeyMap = new PropertyMap { Property = customerKey, ColumnName = "CustomerID" };
        var customerCompanyMap = new PropertyMap { Property = customerCompany, ColumnName = "CompanyID" };

        var customers = new EntityMap
        {
            Entity = new Entity { Name = "Customer", Properties = [customerCompany, customerKey, name] },
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
            Entity = new Entity { Name = "Order", Properties = [orderCompany, orderCustomer, total] },
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
            ColumnPairs = pairs,
        });

        return (orders, customers);
    }

    /// <summary>The query in parentheses before its terminal, which is how a query expression has to be written.</summary>
    private static string Method(string query) =>
        $$"""
        public void Query()
        {
            var q = ({{query}}).ToList();
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string source, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
    {
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return artifacts.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;
    }

    private static void AssertClean(AbstractQueryBuilder builder)
        => Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

    // ---- the path is read as the join the relation derives ------------------------------

    [Fact]
    public void ASecondFromOverACollectionOfTheRowIsTheJoinTheRelationDerives()
    {
        var (orders, customers) = Linked();
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            Method("from c in ctx.Customers from o in c.Orders where o.Total > 100 select new { c.CustomerName, o.Total }"),
            orders,
            customers);

        // Rule Q7: FK(left) = PK(right). A collection navigation is the inverse side of the
        // relation, so the foreign key sits on the joined entity - the same condition the
        // HQL parser derives for `from Customer c join c.Orders o`.
        var sql = Sql(builder);
        AssertClean(builder);

        Assert.Contains("INNER JOIN Sales.Orders o ON o.CustomerID = c.CustomerID", sql);
        Assert.Contains("WHERE o.Total > 100", sql);
        Assert.Contains("c.CustomerName AS CustomerName, o.Total AS Total", sql);
    }

    [Fact]
    public void TheChainSaysTheSameAsTheQueryExpression()
    {
        var (orders, customers) = Linked();
        var expression = Sql(Parse(
            new DapperSqlQueryBuilder(),
            Method("from c in ctx.Customers from o in c.Orders where o.Total > 100 select new { c.CustomerName, o.Total }"),
            orders,
            customers));
        var chain = Sql(Parse(
            new DapperSqlQueryBuilder(),
            Method("ctx.Customers.SelectMany(c => c.Orders, (c, o) => new { c, o }).Where(t => t.o.Total > 100).Select(t => new { t.c.CustomerName, t.o.Total })"),
            orders,
            customers));

        Assert.Equal(expression, chain, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ACompositeKeyJoinsOverEveryPairInTheirOrder()
    {
        var (orders, customers) = Linked(composite: true);
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in ctx.Customers from o in c.Orders select o"), orders, customers);

        var sql = Sql(builder);

        Assert.Matches(@"ON o\.CompanyID = c\.CompanyID AND o\.CustomerID = c\.CustomerID", sql);
    }

    [Fact]
    public void TheClausesAfterTheSecondFromReachBothRows()
    {
        var (orders, customers) = Linked();
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            Method("from c in ctx.Customers from o in c.Orders where c.CustomerName == \"Alice\" orderby o.Total descending select o.Total"),
            orders,
            customers);

        var sql = Sql(builder);
        AssertClean(builder);

        Assert.Contains("WHERE c.CustomerName = 'Alice'", sql);
        Assert.Contains("ORDER BY o.Total DESC", sql);
    }

    [Fact]
    public void ASecondFromAfterAJoinStartsFromTheRowTheJoinBroughtIn()
    {
        var (orders, customers) = Linked();
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            Method("from o in ctx.Orders join c in ctx.Customers on o.CustomerID equals c.CustomerID from o2 in c.Orders select o2.Total"),
            orders,
            customers);

        // The navigation is written through the joined row (t.c.Orders after the rewrite),
        // so the relation is the customer's and the path alias is the customer's.
        var sql = Sql(builder);

        Assert.Contains("INNER JOIN Sales.Customers c ON o.CustomerID = c.CustomerID", sql);
        Assert.Contains("INNER JOIN Sales.Orders o2 ON o2.CustomerID = c.CustomerID", sql);
    }

    [Fact]
    public void ASelectManyWithoutAResultSelectorMaterializesTheCollectionAloneAndSaysSo()
    {
        var (orders, customers) = Linked();
        var builder = Parse(new DapperSqlQueryBuilder(), Method("ctx.Customers.SelectMany(c => c.Orders)"), orders, customers);

        // The rows are the orders alone: one side of the join, which the representation
        // does not carry (decision 048) - the whole joined row goes out and the record says
        // which row the source left out, as for a result selector that names one side.
        var sql = Sql(builder);

        Assert.Contains("INNER JOIN Sales.Orders orders ON orders.CustomerID = c.CustomerID", sql);
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Projection && r.Reason.Contains("leaves out the row 'c'"));
    }

    [Fact]
    public void TheNHibernateLinqParserReadsThePathTheSameWay()
    {
        var (orders, customers) = Linked();
        var builder = new DapperSqlQueryBuilder { EntityMaps = [orders, customers] };
        new NHibernateLinqQueryParser(() => builder).Parse(
            ConversionContentType.CSharpQuery,
            Method("session.Query<Customer>().SelectMany(c => c.Orders, (c, o) => new { c, o }).Where(t => t.o.Total > 100)"),
            [orders, customers]);

        var sql = Sql(builder);

        Assert.Contains("INNER JOIN Sales.Orders o ON o.CustomerID = c.CustomerID", sql);
    }

    [Fact]
    public void TheEFCoreArtifactOfThePathReadsBackToTheSameSql()
    {
        var (orders, customers) = Linked();
        var source = Method("from c in ctx.Customers from o in c.Orders where o.Total > 100 select new { c.CustomerName, o.Total }");

        // No builder writes the path (decision 101): the EF Core target writes the join
        // with the derived key selectors, outer key first as a Join is written, so the
        // artifact reads back to what the same query says with its join written out - the
        // same rows (decision 065), with the equality oriented the way a Join orients it.
        var efCore = Parse(new EFCoreLinqQueryBuilder(), source, orders, customers);
        var artifact = efCore.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        AssertClean(efCore);

        var readBack = Sql(Parse(new DapperSqlQueryBuilder(), artifact, orders, customers));
        var written = Sql(Parse(
            new DapperSqlQueryBuilder(),
            Method("from c in ctx.Customers join o in ctx.Orders on c.CustomerID equals o.CustomerID where o.Total > 100 select new { c.CustomerName, o.Total }"),
            orders,
            customers));

        Assert.Equal(written, readBack, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);
    }

    // ---- what the maps do not hold is refused by name -----------------------------------

    [Theory]
    [InlineData("from c in ctx.Customers from o in c.Orders select o", false, "the mapping of the entity behind 'c'")]
    [InlineData("from c in ctx.Customers from s in c.Shippers select s", true, "names no association")]
    [InlineData("from c in ctx.Customers from r in c.Orders.Lines select r", true, "crosses more than one association")]
    [InlineData("from c in ctx.Customers from o in ctx.Orders select o", true, "cross join")]
    [InlineData("ctx.Customers.SelectMany(c => c.Orders.Where(o => o.Total > 0))", true, "not an association path")]
    public void APathTheMapsDoNotResolveRefusesByName(string query, bool withMaps, string reason)
    {
        var (orders, customers) = Linked();
        var builder = withMaps
            ? Parse(new DapperSqlQueryBuilder(), Method(query), orders, customers)
            : Parse(new DapperSqlQueryBuilder(), Method(query));

        // Nothing is guessed (decision 067) and a query without its join would return
        // different rows (decision 070): no artifact, and the record names the path and
        // what is missing.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains(reason));
    }

    [Fact]
    public void APathWithoutResolvedColumnsRefusesRatherThanGuesses()
    {
        var (orders, customers) = Linked(withPairs: false);
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in ctx.Customers from o in c.Orders select o"), orders, customers);

        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("no foreign key columns"));
    }

    [Fact]
    public void APathAcrossAManyToManyRefuses()
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

        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in ctx.Customers from f in c.Favourites select f"), orders, customers);

        // Two joins over the junction entity of decision 005 and an alias nobody wrote: a
        // stated limit of decision 101, refused by name.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("many-to-many"));
    }

    [Fact]
    public void ASelectManyAfterDistinctDoesNotCommuteWithTheCollapse()
    {
        var (orders, customers) = Linked();
        var builder = Parse(
            new DapperSqlQueryBuilder(),
            Method("ctx.Customers.Distinct().SelectMany(c => c.Orders, (c, o) => new { c, o })"),
            orders,
            customers);

        // A join after Distinct() multiplies the collapsed rows, as decision 073 says of
        // Join; SelectMany is a join and stands in the same enumeration.
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.Join && r.Reason.Contains("after Distinct()"));
    }
}
