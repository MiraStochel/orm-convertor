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
/// A LINQ query expression - <c>from c in ctx.Customers where … select …</c> - is read as
/// the method chain the C# specification defines it as (decision 103). Until 2026-09-29
/// the shared LINQ parser read a chain and nothing else, so the query syntax, which is how
/// a large part of EF Core code and nearly every join is written, ended in "No LINQ query
/// chain was found". Every test here holds the query expression against the chain it
/// rewrites to: the same SQL, the same records - because after the rewrite the parser sees
/// one shape, and everything the chain tests prove holds for the query syntax too.
/// </summary>
public class LinqQueryExpressionTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var properties = columns.Select(c => new Property { Name = c.Name, Type = LangType.Scalar(c.Type) }).ToList();

        return new EntityMap
        {
            Entity = new Entity { Name = entity, Properties = properties },
            Table = table,
            Schema = "Sales",
            PropertyMaps = properties.Select(p => new PropertyMap { Property = p, ColumnName = p.Name }).ToList(),
        };
    }

    private static EntityMap[] Maps() =>
    [
        Map("OrderLine", "OrderLines",
            ("CompanyId", ScalarType.Int), ("OrderId", ScalarType.Int), ("Description", ScalarType.String), ("Quantity", ScalarType.Int)),
        Map("CustomerOrder", "CustomerOrders",
            ("CompanyId", ScalarType.Int), ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int)),
        Map("Customer", "Customers",
            ("CustomerId", ScalarType.Int), ("Name", ScalarType.String)),
    ];

    /// <summary>The query in parentheses before its terminal, which is how a query expression has to be written.</summary>
    private static string Method(string query) =>
        $$"""
        public void Query()
        {
            var q = ({{query}}).ToList();
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string source, bool nhibernate = false)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        IQueryParser parser = nhibernate
            ? new NHibernateLinqQueryParser(() => builder)
            : new EFCoreLinqQueryParser(() => builder);
        parser.Parse(ConversionContentType.CSharpQuery, source, maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

    private static string SqlOf(string query) => Sql(Parse(new DapperSqlQueryBuilder(), Method(query)));

    private static void AssertSql(string expected, string actual)
        => Assert.Equal(expected, actual, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);

    private static void AssertClean(AbstractQueryBuilder builder)
        => Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

    /// <summary>The query expression and the chain it is short for read to the same SQL, cleanly.</summary>
    private static void AssertSameAsChain(string query, string chain)
    {
        var fromQuery = Parse(new DapperSqlQueryBuilder(), Method(query));
        var fromChain = Parse(new DapperSqlQueryBuilder(), Method(chain));

        AssertSql(Sql(fromChain), Sql(fromQuery));
        AssertClean(fromChain);
        AssertClean(fromQuery);
    }

    // ---- one range variable --------------------------------------------------------------

    [Fact]
    public void ADegenerateQuerySelectsTheWholeEntity()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in ctx.Customers select c"));

        AssertSql(
            """
            SELECT *
            FROM Sales.Customers AS c
            """,
            Sql(builder));
        AssertClean(builder);
    }

    [Fact]
    public void WhereIsAWhere()
        => AssertSameAsChain(
            "from c in ctx.Customers where c.Name == \"A\" && c.CustomerId > 1 select c",
            "ctx.Customers.Where(c => c.Name == \"A\" && c.CustomerId > 1)");

    [Fact]
    public void OrderByWithSeveralKeysIsOrderByThenBy()
        => AssertSameAsChain(
            "from c in ctx.Customers orderby c.Name, c.CustomerId descending select c",
            "ctx.Customers.OrderBy(c => c.Name).ThenByDescending(c => c.CustomerId)");

    [Fact]
    public void SelectIsTheProjection()
        => AssertSameAsChain(
            "from c in ctx.Customers where c.CustomerId > 1 select new { c.Name, Id = c.CustomerId }",
            "ctx.Customers.Where(c => c.CustomerId > 1).Select(c => new { c.Name, Id = c.CustomerId })");

    /// <summary>The type of a typed range variable says what the elements are, which the rows of an entity set are already.</summary>
    [Fact]
    public void ATypedRangeVariableReadsLikeAnUntypedOne()
        => AssertSameAsChain(
            "from Customer c in ctx.Customers where c.Name == \"A\" select c",
            "ctx.Customers.Where(c => c.Name == \"A\")");

    [Fact]
    public void AQueryOverTheNHibernateRootIsRead()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in session.Query<Customer>() where c.Name == \"A\" select c"), nhibernate: true);

        AssertSql(
            """
            SELECT *
            FROM Sales.Customers AS c
            WHERE c.Name = 'A'
            """,
            Sql(builder));
        AssertClean(builder);
    }

    // ---- joins and the transparent identifier --------------------------------------------

    private const string TwoColumnJoin = """
        ctx.OrderLines
            .Join(ctx.CustomerOrders,
                ol => new { ol.CompanyId, ol.OrderId },
                o => new { o.CompanyId, o.OrderId },
        """;

    /// <summary>A join directly before the select takes the selected shape as its result selector.</summary>
    [Fact]
    public void AJoinBeforeTheSelectTakesItAsTheResultSelector()
        => AssertSameAsChain(
            """
            from ol in ctx.OrderLines
            join o in ctx.CustomerOrders on new { ol.CompanyId, ol.OrderId } equals new { o.CompanyId, o.OrderId }
            select new { ol.Description, Customer = o.CustomerId }
            """,
            TwoColumnJoin + "(ol, o) => new { ol.Description, Customer = o.CustomerId })");

    /// <summary>
    /// A clause after a join ranges over both rows under a transparent identifier: the
    /// chain the rewrite yields composes them as <c>(ol, o) =&gt; new { ol, o }</c> and
    /// reaches a column as <c>t.o.CustomerId</c>, which is the joined row the parser reads.
    /// </summary>
    [Fact]
    public void AClauseAfterAJoinRangesOverTheComposedRow()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            """
            from ol in ctx.OrderLines
            join o in ctx.CustomerOrders on ol.OrderId equals o.OrderId
            where o.CustomerId > 0 && ol.Quantity > 1
            orderby o.OrderId
            select new { Text = ol.Description, o.CustomerId }
            """));

        AssertSql(
            """
            SELECT ol.Description AS Text, o.CustomerId AS CustomerId
            FROM Sales.OrderLines AS ol
            INNER JOIN Sales.CustomerOrders o ON ol.OrderId = o.OrderId
            WHERE o.CustomerId > 0 AND ol.Quantity > 1
            ORDER BY o.OrderId ASC
            """,
            Sql(builder));
        AssertClean(builder);
    }

    [Fact]
    public void ASecondJoinKeysOnTheTableTheFirstBroughtIn()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            """
            from ol in ctx.OrderLines
            join o in ctx.CustomerOrders on ol.OrderId equals o.OrderId
            join c in ctx.Customers on o.CustomerId equals c.CustomerId
            where c.Name == "A"
            select new { ol.Description, c.Name }
            """));

        var sql = Sql(builder);
        Assert.Contains("INNER JOIN Sales.CustomerOrders o ON ol.OrderId = o.OrderId", sql);
        Assert.Contains("INNER JOIN Sales.Customers c ON o.CustomerId = c.CustomerId", sql);
        Assert.Contains("WHERE c.Name = 'A'", sql);
        Assert.StartsWith("SELECT ol.Description AS Description, c.Name AS Name", sql);
        AssertClean(builder);
    }

    /// <summary>Selecting one range variable after a join is one side of the joined row, the loss a chain gets for it.</summary>
    [Fact]
    public void SelectingOneSideAfterAJoinIsTheSameLossAsInAChain()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            "from ol in ctx.OrderLines join o in ctx.CustomerOrders on ol.OrderId equals o.OrderId where o.CustomerId > 0 select ol"));

        Assert.StartsWith("SELECT *", Sql(builder));
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains("leaves out the row 'o'", record.Reason);
    }

    // ---- grouping and continuations ------------------------------------------------------

    [Fact]
    public void AGroupingWithAContinuationFiltersOrdersAndProjectsTheGroups()
        => AssertSameAsChain(
            """
            from o in ctx.CustomerOrders
            group o by o.CustomerId into g
            where g.Count() > 1
            orderby g.Key
            select new { Customer = g.Key, Orders = g.Count() }
            """,
            "ctx.CustomerOrders.GroupBy(o => o.CustomerId).Where(g => g.Count() > 1).OrderBy(g => g.Key).Select(g => new { Customer = g.Key, Orders = g.Count() })");

    [Fact]
    public void TheGroupingRendersAsGroupByAndHaving()
    {
        var sql = SqlOf("from o in ctx.CustomerOrders group o by o.CustomerId into g where g.Count() > 1 select new { Customer = g.Key, Orders = g.Count() }");

        AssertSql(
            """
            SELECT o.CustomerId AS Customer, COUNT(*) AS Orders
            FROM Sales.CustomerOrders AS o
            GROUP BY o.CustomerId
            HAVING COUNT(*) > 1
            """,
            sql);
    }

    /// <summary>
    /// <c>group o.OrderId by o.CustomerId</c> is GroupBy(key, element): the groups are the
    /// same rows, what the element selector changes has no place in the representation,
    /// and it is said (decision 048) rather than skipped.
    /// </summary>
    [Fact]
    public void AGroupedElementOtherThanTheRangeVariableIsALoss()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from o in ctx.CustomerOrders group o.OrderId by o.CustomerId into g select g.Key"));

        Assert.Contains("GROUP BY o.CustomerId", Sql(builder));
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(QueryFeature.Grouping, record.Feature);
        Assert.Contains("Only the key selector of GroupBy() was read", record.Reason);
    }

    [Fact]
    public void ASelectContinuationStartsOverTheProjectedResult()
        => AssertSameAsChain(
            "from c in ctx.Customers select c into x orderby x.Name select x",
            "ctx.Customers.Select(c => c).OrderBy(x => x.Name).Select(x => x)");

    // ---- nesting and terminals -----------------------------------------------------------

    /// <summary>An inner query expression is a chain by the time the clause around it is read, so it is the subquery a chain would be.</summary>
    [Fact]
    public void ANestedQueryExpressionIsASubquery()
    {
        var sql = SqlOf(
            "from c in ctx.Customers where (from o in ctx.CustomerOrders where o.CustomerId == c.CustomerId select o).Any() select c");

        Assert.Contains("EXISTS", sql);
        Assert.Contains("o.CustomerId = c.CustomerId", sql);
    }

    /// <summary>The parentheses a query expression needs before a terminal are no link of the chain.</summary>
    [Fact]
    public void AQueryExpressionMayEndInASingleRowTerminal()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), "public void Query() { var q = (from c in ctx.Customers where c.Name == \"A\" select c).FirstOrDefault(); }");
        var chain = Parse(new DapperSqlQueryBuilder(), Method("ctx.Customers.Where(c => c.Name == \"A\").Take(1)"));

        AssertSql(Sql(chain), Sql(builder));
        Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
    }

    // ---- what is refused, by name --------------------------------------------------------

    [Fact]
    public void ALetClauseIsRefusedByNameAndNothingElseIsSaid()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method("from c in ctx.Customers let n = c.Name where n == \"A\" select c"));

        Assert.Empty(builder.Build());
        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Failure, record.Kind);
        Assert.Equal(QueryFeature.Projection, record.Feature);
        Assert.Contains("'let n = c.Name'", record.Reason);
    }

    [Fact]
    public void AGroupJoinIsRefusedAsAJoin()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            "from c in ctx.Customers join o in ctx.CustomerOrders on c.CustomerId equals o.CustomerId into orders select new { c.Name, N = orders.Count() }"));

        Assert.Empty(builder.Build());
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(QueryFeature.Join, record.Feature);
        Assert.StartsWith("GroupJoin()", record.Reason);
    }

    [Fact]
    public void ASecondFromIsRefusedAsAJoin()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            "from c in ctx.Customers from o in ctx.CustomerOrders where o.CustomerId == c.CustomerId select o"));

        // The clause after it ranges over a row the refused step never composed, so it is
        // refused too; the step itself is the reason that names what was written.
        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure
                                              && r.Feature == QueryFeature.Join
                                              && r.Reason.StartsWith("SelectMany()"));
    }

    // ---- the round trip through the EF Core builder -------------------------------------

    [Fact]
    public void TheEFCoreArtifactOfAQueryExpressionReadsBackToTheSameSql()
    {
        var source = Method(
            """
            from ol in ctx.OrderLines
            join o in ctx.CustomerOrders on ol.OrderId equals o.OrderId
            where o.CustomerId > 0
            orderby ol.OrderId
            select new { Text = ol.Description, o.CustomerId }
            """);

        var efCore = Parse(new EFCoreLinqQueryBuilder(), source);
        var artifact = efCore.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        AssertClean(efCore);

        var readBack = Parse(new DapperSqlQueryBuilder(), artifact);
        AssertClean(readBack);
        AssertSql(Sql(Parse(new DapperSqlQueryBuilder(), source)), Sql(readBack));
    }
}
