using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Combined;

/// <summary>
/// The result selector of a LINQ join and the joined row it shapes. Until 2026-09-29 the
/// shared LINQ parser read the source, the two key selectors and the kind of a join and
/// nothing else: <c>(ol, o) =&gt; new { ol.Description }</c> came out as every column of
/// both tables without a record, which is the silence decision 048 forbids, and a step after
/// the join could reach a column only one level deep (<c>x.Column</c>), so the two-level
/// access valid C# writes over a joined row (<c>x.o.CustomerId</c>) sank the clause - the EF
/// Core builder's own output included, so EF Core → EF Core over a join with a filter on the
/// joined table did not read back.
///
/// Both halves are here: the selector read as the whole joined row or as the projection,
/// with a loss record for the shapes the representation does not carry (decision 070: the
/// row set is the same, the columns are not), and the members of the joined row resolved in
/// every clause after the join - filter, ordering, grouping, projection, aggregate, the key
/// of a further join, and a correlated reference from a nested chain.
/// </summary>
public class LinqJoinResultSelectorTest
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

    private static string Method(string chain) =>
        $$"""
        public void Query()
        {
            var q = {{chain}}.ToList();
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string source)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, maps);
        return builder;
    }

    private static string Sql(AbstractQueryBuilder builder)
        => builder.Build().Single(s => s.ContentType == ConversionContentType.SqlQuery).Content;

    private static string SqlOf(string chain) => Sql(Parse(new DapperSqlQueryBuilder(), Method(chain)));

    private static void AssertSql(string expected, string actual)
        => Assert.Equal(expected, actual, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);

    private const string TwoColumnJoin = """
        ctx.OrderLines
            .Join(ctx.CustomerOrders,
                ol => new { ol.CompanyId, ol.OrderId },
                o => new { o.CompanyId, o.OrderId },
        """;

    // ---- the selector -------------------------------------------------------------------

    [Fact]
    public void ColumnsInTheSelectorAreTheProjection()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(TwoColumnJoin + "(ol, o) => new { ol.Description, Customer = o.CustomerId })"));

        AssertSql(
            """
            SELECT ol.Description AS Description, o.CustomerId AS Customer
            FROM Sales.OrderLines AS ol
            INNER JOIN Sales.CustomerOrders o ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            """,
            Sql(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
    }

    [Fact]
    public void BothRowsInTheSelectorAreTheWholeJoinedRowAndNoRecord()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(TwoColumnJoin + "(ol, o) => new { ol, o })"));

        Assert.StartsWith("SELECT *", Sql(builder));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
    }

    /// <summary>The names are the selector's to give; what they name is what counts.</summary>
    [Fact]
    public void TheRowsMayBeRenamedInTheSelector()
    {
        var sql = SqlOf(TwoColumnJoin + "(ol, o) => new { Line = ol, Order = o }).Where(x => x.Order.CustomerId > 0).Select(x => x.Line.Description)");

        Assert.Contains("SELECT ol.Description AS Description", sql);
        Assert.Contains("WHERE o.CustomerId > 0", sql);
    }

    [Fact]
    public void OneSideAloneIsALossWithTheArtifact()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(TwoColumnJoin + "(ol, o) => ol)"));

        Assert.StartsWith("SELECT *", Sql(builder));
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(QueryFeature.Projection, record.Feature);
        Assert.Contains("leaves out the row 'o'", record.Reason);
    }

    [Fact]
    public void AWholeRowBesideColumnsIsALossAndTheColumnsAreProjected()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(TwoColumnJoin + "(ol, o) => new { ol, o.CustomerId })"));

        Assert.StartsWith("SELECT o.CustomerId AS CustomerId", Sql(builder));
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(QueryFeature.Projection, record.Feature);
        Assert.Contains("whole row 'ol' beside columns", record.Reason);
    }

    [Fact]
    public void ARowLeftOutOfALaterSelectorIsALoss()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Method(
            TwoColumnJoin + "(ol, o) => new { ol, o })"
            + ".Join(ctx.Customers, x => x.o.CustomerId, c => c.CustomerId, (x, c) => new { x.ol, c })"));

        Assert.StartsWith("SELECT *", Sql(builder));
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains("leaves out the row 'o'", record.Reason);
    }

    // ---- the joined row after the join --------------------------------------------------

    [Fact]
    public void AFilterAnOrderingAndAProjectionReachTheJoinedTable()
    {
        AssertSql(
            """
            SELECT ol.Description AS Text, o.CustomerId AS CustomerId
            FROM Sales.OrderLines AS ol
            INNER JOIN Sales.CustomerOrders o ON ol.CompanyId = o.CompanyId AND ol.OrderId = o.OrderId
            WHERE o.CustomerId > 0 AND ol.Quantity > 1
            ORDER BY o.OrderId ASC
            """,
            SqlOf(TwoColumnJoin + """
                (ol, o) => new { ol, o })
                .Where(x => x.o.CustomerId > 0 && x.ol.Quantity > 1)
                .OrderBy(x => x.o.OrderId)
                .Select(x => new { Text = x.ol.Description, x.o.CustomerId })
                """));
    }

    [Fact]
    public void ASecondJoinKeysOnTheTableTheFirstBroughtIn()
    {
        var sql = SqlOf(
            TwoColumnJoin + "(ol, o) => new { ol, o })"
            + ".Join(ctx.Customers, x => x.o.CustomerId, c => c.CustomerId, (x, c) => new { x.ol, x.o, c })"
            + ".Where(x => x.c.Name == \"A\")");

        Assert.Contains("INNER JOIN Sales.Customers c ON o.CustomerId = c.CustomerId", sql);
        Assert.Contains("WHERE c.Name = 'A'", sql);
    }

    [Fact]
    public void AGroupingAndAnAggregateAfterTheJoinNameTheirTables()
    {
        var sql = SqlOf(TwoColumnJoin + """
            (ol, o) => new { ol, o })
            .GroupBy(x => x.o.CustomerId)
            .Select(g => new { Customer = g.Key, Total = g.Sum(x => x.ol.Quantity) })
            """);

        Assert.Contains("SUM(ol.Quantity) AS Total", sql);
        Assert.Contains("GROUP BY o.CustomerId", sql);
    }

    /// <summary>
    /// A nested chain reaches the joined row of the chain it sits in the way it reaches the
    /// outer alias (decision 061).
    /// </summary>
    [Fact]
    public void ACorrelatedReferenceReachesTheJoinedRow()
    {
        var sql = SqlOf(TwoColumnJoin + """
            (ol, o) => new { ol, o })
            .Where(x => ctx.Customers.Any(c => c.CustomerId == x.o.CustomerId && c.Name != null))
            """);

        Assert.Contains("c.CustomerId = o.CustomerId", sql);
    }

    /// <summary>
    /// The joined table is named after the result selector's parameter; the table's own
    /// name is the fallback for a selector that is not read, and stays unique.
    /// </summary>
    [Fact]
    public void TheJoinedTableTakesTheSelectorsNameAndFallsBackToTheTables()
    {
        Assert.Contains("INNER JOIN Sales.CustomerOrders o ON", SqlOf(TwoColumnJoin + "(ol, o) => new { ol, o })"));
        Assert.Contains("INNER JOIN Sales.CustomerOrders customerorders ON", SqlOf(TwoColumnJoin + "Materialize)"));
    }

    // ---- the round trip through the EF Core builder -------------------------------------

    /// <summary>
    /// The EF Core builder writes the joined row as <c>(ol, o) =&gt; new { ol, o }</c> and
    /// reaches its members as <c>t.o.CustomerId</c>; the parser has to read that back for
    /// EF Core → EF Core to be an identity over a join.
    /// </summary>
    [Fact]
    public void TheEFCoreArtifactOverAJoinReadsBackToTheSameSql()
    {
        var source = Method(TwoColumnJoin + """
            (ol, o) => new { ol, o })
            .Where(x => x.o.CustomerId > 0)
            .OrderBy(x => x.ol.OrderId)
            .Select(x => new { Text = x.ol.Description, x.o.CustomerId })
            """);

        var efCore = Parse(new EFCoreLinqQueryBuilder(), source);
        var artifact = efCore.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.DoesNotContain(efCore.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

        var readBack = Parse(new DapperSqlQueryBuilder(), artifact);
        Assert.DoesNotContain(readBack.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
        AssertSql(Sql(Parse(new DapperSqlQueryBuilder(), source)), Sql(readBack));
    }
}
