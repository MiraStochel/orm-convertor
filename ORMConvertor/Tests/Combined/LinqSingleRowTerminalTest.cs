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
/// A single-row terminal of a LINQ chain - First(), FirstOrDefault(), Single(),
/// SingleOrDefault(), ElementAt() and the async forms EF Core adds - is read as the slice of
/// one row it selects (decision 103): Take(1), with the predicate form putting a Where() in
/// front and ElementAt(n) an offset. The rows are the same; what the terminal says beyond
/// them - one object instead of a list, for Single() the check that there is no second row
/// - is a fact of the calling code, and a convention record names it. Until 2026-09-29 all
/// of them were refused as steps the representation does not carry, which refused the most
/// common terminal of real EF Core code. Last() stays refused: it is the row of the reversed
/// ordering, not a slice of the rows as ordered.
/// </summary>
public class LinqSingleRowTerminalTest
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
        Map("CustomerOrder", "CustomerOrders",
            ("OrderId", ScalarType.Int), ("CustomerId", ScalarType.Int)),
        Map("Customer", "Customers",
            ("CustomerId", ScalarType.Int), ("Name", ScalarType.String)),
    ];

    private static string Statement(string expression) =>
        $$"""
        public void Query()
        {
            var q = {{expression}};
        }
        """;

    private static string AsyncStatement(string expression) =>
        $$"""
        public async Task Query()
        {
            var q = await {{expression}};
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

    private static void AssertSql(string expected, string actual)
        => Assert.Equal(expected, actual, ignoreWhiteSpaceDifferences: true, ignoreLineEndingDifferences: true);

    private static void AssertNoRefusalOrLoss(AbstractQueryBuilder builder)
        => Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);

    /// <summary>The terminal reads to the SQL of the slice it stands for, with one convention record about it.</summary>
    private static ConversionRecord AssertSliceOf(string source, string slice)
    {
        var terminal = Parse(new DapperSqlQueryBuilder(), source);
        var expanded = Parse(new DapperSqlQueryBuilder(), Statement(slice));

        AssertSql(Sql(expanded), Sql(terminal));
        AssertNoRefusalOrLoss(terminal);
        AssertNoRefusalOrLoss(expanded);

        var record = Assert.Single(terminal.Records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("is carried as"));
        Assert.Equal(QueryFeature.Pagination, record.Feature);
        return record;
    }

    // ---- the slice --------------------------------------------------------------------------

    [Fact]
    public void FirstOrDefaultIsTheSliceOfOneRow()
    {
        var record = AssertSliceOf(
            Statement("ctx.Customers.OrderBy(c => c.Name).FirstOrDefault()"),
            "ctx.Customers.OrderBy(c => c.Name).Take(1)");

        Assert.StartsWith("FirstOrDefault() is carried as Take(1)", record.Reason);
        Assert.Contains("single value rather than as a list of one", record.Reason);
    }

    [Fact]
    public void ThePredicateFormIsAWhereBeforeTheSlice()
    {
        AssertSliceOf(
            Statement("ctx.Customers.First(c => c.Name == \"A\")"),
            "ctx.Customers.Where(c => c.Name == \"A\").Take(1)");

        // The predicate's parameter names the source, as the first lambda of any chain does.
        Assert.Contains("WHERE c.Name = 'A'", Sql(Parse(new DapperSqlQueryBuilder(), Statement("ctx.Customers.First(c => c.Name == \"A\")"))));
    }

    [Fact]
    public void SingleNamesTheCheckTheArtifactDoesNotCarry()
    {
        var record = AssertSliceOf(
            Statement("ctx.Customers.SingleOrDefault(c => c.CustomerId == id)"),
            "ctx.Customers.Where(c => c.CustomerId == id).Take(1)");

        Assert.StartsWith("SingleOrDefault() is carried as Take(1)", record.Reason);
        Assert.Contains("no second row", record.Reason);
    }

    [Fact]
    public void TheAsyncFormReadsAlike()
    {
        var record = AssertSliceOf(
            AsyncStatement("ctx.Customers.FirstOrDefaultAsync(c => c.Name == \"A\")"),
            "ctx.Customers.Where(c => c.Name == \"A\").Take(1)");

        Assert.StartsWith("FirstOrDefaultAsync() is carried as Take(1)", record.Reason);
    }

    [Fact]
    public void ElementAtIsTheSliceAtAnOffset()
    {
        var record = AssertSliceOf(
            Statement("ctx.Customers.OrderBy(c => c.Name).ElementAt(3)"),
            "ctx.Customers.OrderBy(c => c.Name).Skip(3).Take(1)");

        Assert.StartsWith("ElementAt() is carried as Skip(n).Take(1)", record.Reason);
    }

    [Fact]
    public void ATerminalAfterSkipIsTheOffsetAndTheRow()
        => AssertSliceOf(
            Statement("ctx.Customers.OrderBy(c => c.Name).Skip(10).First()"),
            "ctx.Customers.OrderBy(c => c.Name).Skip(10).Take(1)");

    /// <summary>A terminal over a projection is the slice of the projected rows.</summary>
    [Fact]
    public void ATerminalAfterAProjectionSlicesTheProjection()
        => AssertSliceOf(
            Statement("ctx.Customers.OrderBy(c => c.Name).Select(c => c.Name).FirstOrDefault()"),
            "ctx.Customers.OrderBy(c => c.Name).Select(c => c.Name).Take(1)");

    // ---- the round trip through the EF Core builder ----------------------------------------

    [Fact]
    public void TheEFCoreArtifactCarriesTheSliceAndReadsBack()
    {
        var source = Statement("ctx.Customers.OrderBy(c => c.Name).FirstOrDefault(c => c.Name == \"A\")");

        var efCore = Parse(new EFCoreLinqQueryBuilder(), source);
        var artifact = efCore.Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;
        Assert.Contains(".Take(1)", artifact);
        AssertNoRefusalOrLoss(efCore);

        var readBack = Parse(new DapperSqlQueryBuilder(), artifact);
        AssertNoRefusalOrLoss(readBack);
        Assert.DoesNotContain(readBack.Records, r => r.Kind == ConversionRecordKind.Convention);
        AssertSql(Sql(Parse(new DapperSqlQueryBuilder(), source)), Sql(readBack));
    }

    // ---- what stays refused, by name -------------------------------------------------------

    private static ConversionRecord AssertRefused(string source, QueryFeature feature)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), source);

        Assert.Empty(builder.Build());
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal(feature, record.Feature);
        return record;
    }

    [Fact]
    public void ATerminalAfterTakeWouldSliceASlice()
    {
        var record = AssertRefused(Statement("ctx.Customers.Take(5).First()"), QueryFeature.Pagination);

        Assert.StartsWith("First() after Take() would slice a slice", record.Reason);
    }

    /// <summary>The predicate does not commute with the slice before it, and the terminal is refused once, not once per step it expands to.</summary>
    [Fact]
    public void ThePredicateFormAfterTakeIsRefusedOnce()
    {
        var record = AssertRefused(Statement("ctx.Customers.Take(5).First(c => c.Name == \"A\")"), QueryFeature.Pagination);

        Assert.StartsWith("First() after Skip() or Take() does not commute", record.Reason);
    }

    [Fact]
    public void ATerminalAfterASetOperationIsRefused()
    {
        var record = AssertRefused(Statement("ctx.Customers.Union(ctx.Customers).First()"), QueryFeature.Pagination);

        Assert.StartsWith("First() applied after a set operation", record.Reason);
    }

    [Fact]
    public void LastStaysRefusedAndSaysWhy()
    {
        var record = AssertRefused(Statement("ctx.Customers.OrderBy(c => c.Name).Last()"), QueryFeature.Pagination);

        Assert.StartsWith("Last() selects the last row of the ordering", record.Reason);
    }

    /// <summary>The async form of a refused terminal is the same terminal, not an unknown step left out with a loss.</summary>
    [Fact]
    public void AnAsyncAggregateTerminalIsRefusedNotLost()
    {
        var record = AssertRefused(AsyncStatement("ctx.Customers.CountAsync()"), QueryFeature.Aggregation);

        Assert.StartsWith("CountAsync()", record.Reason);
    }

    /// <summary>
    /// The terminal is read at the end of the chain of the query itself. In operand position
    /// it would be a scalar subquery with a slice, which the operand reader does not read;
    /// the predicate around it is refused as before (decision 070).
    /// </summary>
    [Fact]
    public void ATerminalInOperandPositionStaysUnread()
        => AssertRefused(
            Statement("ctx.Customers.Where(c => c.CustomerId == ctx.CustomerOrders.Select(o => o.CustomerId).FirstOrDefault())"),
            QueryFeature.Filtering);
}
