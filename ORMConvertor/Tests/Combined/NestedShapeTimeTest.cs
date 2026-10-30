using System.Text;
using System.Text.RegularExpressions;
using AbstractWrappers;
using DapperWrappers;
using Model;
using OrmConvertor;
using Tests.Experiment;

namespace Tests.Combined;

/// <summary>
/// S3 along one axis of nesting: a shape that repeats one construct inside itself costs time
/// in proportion to its size, never to two to the power of its depth. Each shape here once did -
/// a walk that took the subtree below every level again - and each holds a depth at which that
/// cost would not finish for hours. A walk over the bound is left running rather than awaited,
/// so that the test fails at the bound instead of hanging; the sum in the projection is held
/// by <c>Experiment/LimitSeriesTest</c>.
/// </summary>
public class NestedShapeTimeTest
{
    private static readonly TimeSpan S3Bound = TimeSpan.FromSeconds(30);

    private static async Task<T> WithinTheBound<T>(string shape, Func<T> walk)
    {
        var running = Task.Run(walk, TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(running, Task.Delay(S3Bound, TestContext.Current.CancellationToken));
        Assert.True(first == running, $"{shape} did not finish within the 30 s of S3.");
        return await running;
    }

    /// <summary>
    /// A chain of intermediate results, each reading the one before twice: the demand on the
    /// catalog follows a parameter's column back to the table behind the first link, and walks
    /// each link once.
    /// </summary>
    [Fact]
    public async Task TheCatalogDemandOfAChainOfDefinitionsWalksEachLinkOnce()
    {
        const int links = 40;
        var sql = new StringBuilder("WITH d0 AS (SELECT o.Total AS x FROM Shop.Orders AS o)");
        for (var i = 1; i <= links; i++)
        {
            sql.Append($", d{i} AS (SELECT a.x + a.x AS x FROM d{i - 1} AS a)");
        }

        sql.Append($" SELECT a.x FROM d{links} AS a WHERE a.x = @p");

        var builder = new DapperSqlQueryBuilder();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql.ToString());

        var demand = await WithinTheBound($"The demand of {links} chained definitions", builder.CatalogDemand);

        Assert.Single(demand);
        Assert.Contains(demand, d => d.Names(QueryTableDemand.Of("Shop.Orders")));
    }

    /// <summary>
    /// A chain of intermediate results from an EF Core source, each variable continuing the one
    /// before (decision 112): what a variable holds is decided once per variable, so the reading
    /// walks each link a bounded number of times rather than the chain below it again at every
    /// link. The input is a point of the ctes series of the limits run.
    /// </summary>
    [Fact]
    public async Task AChainOfQueryVariablesIsFollowedOncePerLink()
    {
        const int links = 48;
        var units = LimitSeries.Axis("ctes")!.Units(ORMEnum.EFCore, links);

        var conversion = await WithinTheBound($"A chain of {links} query variables from EF Core", () => ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, units));

        Assert.DoesNotContain(conversion.Records, r => r.Kind == AbstractWrappers.Diagnostics.ConversionRecordKind.Failure);
        var query = Assert.Single(conversion.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.Equal(links, Regex.Matches(query.Content, @"\bc\d+ AS \(").Count);
    }

    /// <summary>
    /// A CASE compared inside the condition of a CASE, nested: the LINQ visitor renders the left
    /// side of each comparison once, so the text and the time grow with the depth.
    /// </summary>
    [Fact]
    public async Task ACaseComparedInsideACaseRendersEachLevelOnceInLinq()
    {
        const int depth = 32;
        var nested = "p.UnitPrice";
        for (var i = 0; i < depth; i++)
        {
            nested = $"CASE WHEN {nested} > 1 THEN 1 END";
        }

        List<ConversionSource> units =
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
            new() { Name = "FindNested.sql", ContentType = ConversionContentType.SqlQuery, Content = $"SELECT p.ProductName AS ProductName FROM {QueryShapeInputs.Schema}.ShopProducts AS p WHERE {nested} > 1" },
        ];

        var conversion = await WithinTheBound($"A CASE nested {depth} deep in the condition of a CASE", () => ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, units));

        Assert.DoesNotContain(conversion.Records, r => r.Kind == AbstractWrappers.Diagnostics.ConversionRecordKind.Failure);
        var query = Assert.Single(conversion.Sources, s => s.ContentType.IsQuery());
        Assert.True(query.Content.Length < 200 * depth, $"The query of a CASE nested {depth} deep is {query.Content.Length} characters long; one level writes a few dozen.");
    }
}
