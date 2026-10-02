using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// EF Core's explicit loading - <c>ctx.Entry(order).Collection(o =&gt; o.Lines).Query()</c>,
/// <c>Reference(…)</c>, <c>Navigation("…")</c>, ended by <c>Query()</c> or <c>Load()</c> - is
/// a query the provider composes: the rows of the navigation's target filtered on the foreign
/// key by the key of the entity in memory (decision 115). It is read as that query, with the
/// relation taken from the mapping and the key as a parameter named after the property the
/// provider binds, recorded as the convention it is; what the mapping lacks refuses the place
/// by name. It used not to be read at all, and the class around it came out as an entity.
/// </summary>
public class ExplicitLoadingTest
{
    private const string File = "Sales.cs";

    private static ConversionResult Convert(ORMEnum target, params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.EFCore, target, [.. units]);

    private static ConversionSource Unit(string content, string name = File)
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static List<ConversionRecord> NotEntities(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.Contains("not as an entity", StringComparison.Ordinal))];

    private const string Entities = """
        public class SalesOrder
        {
            public int SalesOrderID { get; set; }

            public List<OrderLine> Lines { get; set; } = [];
        }

        public class OrderLine
        {
            public int OrderLineID { get; set; }

            public int SalesOrderID { get; set; }

            public decimal Amount { get; set; }

            public SalesOrder Order { get; set; } = null!;
        }
        """;

    private static string Service(string member)
        => Entities + "\n\npublic class LineService(ShopContext ctx)\n{\n    " + member + "\n}\n";

    /// <summary>
    /// The collection of an order the unit declares, loaded explicitly and composed over: the
    /// query is over the lines, filtered by the order's key as a parameter and by the composed
    /// filter, the record says how it was read, and the class around is the code around a
    /// query, not an entity.
    /// </summary>
    [Fact]
    public void ACollectionLoadedExplicitlyIsTheQueryOverItsRowsFilteredByTheKey()
    {
        var result = Convert(ORMEnum.Dapper, Unit(Service(
            "public List<OrderLine> Big(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).Query().Where(l => l.Amount > 100).ToList();")));

        var sql = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("FROM OrderLine", sql, StringComparison.Ordinal);
        Assert.Contains("l.SalesOrderID = @SalesOrderID", sql, StringComparison.Ordinal);
        Assert.Contains("l.Amount > 100", sql, StringComparison.Ordinal);

        var convention = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("explicit load", StringComparison.Ordinal));
        Assert.Contains("'order.Lines'", convention.Reason, StringComparison.Ordinal);
        Assert.Contains("'SalesOrderID'", convention.Reason, StringComparison.Ordinal);

        Assert.Equal(2, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Contains("'LineService'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// Load() and LoadAsync() run the query the provider composes and put the rows into the
    /// navigation; the query is the same, and Navigation("…") names the navigation by text.
    /// </summary>
    [Theory]
    [InlineData("public void Fill(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).Load();")]
    [InlineData("public async Task Fill(SalesOrder order) => await ctx.Entry(order).Collection(o => o.Lines).LoadAsync();")]
    [InlineData("public void Fill(SalesOrder order) => ctx.Entry(order).Navigation(\"Lines\").Load();")]
    [InlineData("public void Fill(SalesOrder order) => this.ctx.Entry(order).Collection(o => o.Lines!).Load();")]
    [InlineData("public IQueryable<OrderLine> All(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).Query();")]
    public void LoadAndQueryReadTheSameQuery(string member)
    {
        var result = Convert(ORMEnum.Dapper, Unit(Service(member)));

        var sql = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("FROM OrderLine", sql, StringComparison.Ordinal);
        Assert.Contains("SalesOrderID = @SalesOrderID", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A reference from the owning side: the line holds the foreign key, so the query is over
    /// the orders, whose key equals the line's foreign key value, bound as a parameter named
    /// after the line's property.
    /// </summary>
    [Fact]
    public void AReferenceFromTheOwningSideFiltersTheTargetKeyByTheForeignKeyValue()
    {
        var result = Convert(ORMEnum.Dapper, Unit(Service(
            "public void Fill(OrderLine line) => ctx.Entry(line).Reference(l => l.Order).Load();")));

        var sql = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("FROM SalesOrder", sql, StringComparison.Ordinal);
        Assert.Contains("SalesOrderID = @SalesOrderID", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// The entity in memory may be known from the run query a local holds the result of, from
    /// the type argument of Entry, or from the one entity of the conversion declaring the
    /// navigation where the unit states nothing about the name.
    /// </summary>
    [Theory]
    [InlineData("public void Fill() { var order = ctx.SalesOrders.First(o => o.SalesOrderID == 7); ctx.Entry(order).Collection(o => o.Lines).Load(); }", 2)]
    [InlineData("public async Task Fill(int id) { var order = await ctx.SalesOrders.FindAsync(id); ctx.Entry(order!).Collection(o => o.Lines).Load(); }", 2)]
    [InlineData("public void Fill(object order) => ctx.Entry<SalesOrder>(order).Collection(o => o.Lines).Load();", 1)]
    [InlineData("public void Fill() => ctx.Entry(Current).Collection(o => o.Lines).Load();", 1)]
    public void TheEntityInMemoryIsTakenFromWhatTheUnitStatesOrFromTheOneNavigationOfTheName(string member, int queries)
    {
        var result = Convert(ORMEnum.Dapper, Unit(Service(member)));

        var sqls = Artifacts(result, ConversionContentType.SqlQuery);
        Assert.Equal(queries, sqls.Count);
        Assert.Contains(sqls, sql => sql.Contains("FROM OrderLine", StringComparison.Ordinal) && sql.Contains("SalesOrderID = @SalesOrderID", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>A Query() kept in a variable assigned once and continued is the beginning of the query that continues it (decision 109).</summary>
    [Fact]
    public void AQueryKeptInAVariableIsReadWithItsContinuation()
    {
        var result = Convert(ORMEnum.Dapper, Unit(Service(
            "public List<OrderLine> Big(SalesOrder order) { var lines = ctx.Entry(order).Collection(o => o.Lines).Query(); return lines.Where(l => l.Amount > 100).ToList(); }")));

        var sql = Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("SalesOrderID = @SalesOrderID", sql, StringComparison.Ordinal);
        Assert.Contains("Amount > 100", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>The read query comes out in EF Core as a chain over Set&lt;T&gt;() with a method parameter, which the identity direction reads back.</summary>
    [Fact]
    public void TheQueryComesOutInEFCoreAsAChainOverTheSetWithAParameter()
    {
        var result = Convert(ORMEnum.EFCore, Unit(Service(
            "public void Fill(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).Load();")));

        var method = Assert.Single(Artifacts(result, ConversionContentType.CSharpQuery));
        Assert.Contains("ctx.Set<OrderLine>()", method, StringComparison.Ordinal);
        Assert.Contains("SalesOrderID == SalesOrderID", method, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// What the mapping lacks refuses the place by name, and the place stays a place: the
    /// class around it is the code around a query, and no query over a table named after the
    /// navigation comes out.
    /// </summary>
    [Theory]
    [InlineData("public void Fill(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).Load();", false, "'SalesOrder'")]
    [InlineData("public void Fill(SalesOrder order, string name) => ctx.Entry(order).Collection(name).Load();", true, "neither a lambda")]
    [InlineData("public void Fill(SalesOrder order) => ctx.Entry(order).Collection(o => o.Tags).Load();", true, "'order.Tags'")]
    [InlineData("public void Fill() => ctx.Entry(Current).Collection(o => o.Lines).Load();", false, "no entity of the conversion declares a navigation 'Lines'")]
    public void WhatTheMappingLacksRefusesThePlaceByName(string member, bool withEntities, string expected)
    {
        var units = new List<ConversionSource> { Unit("public class LineService(ShopContext ctx)\n{\n    " + member + "\n}\n") };
        if (withEntities)
        {
            units.Add(Unit(Entities, "Entities.cs"));
        }

        var result = Convert(ORMEnum.Dapper, [.. units]);

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit is not null);
        Assert.Equal(File, failure.Unit);
        Assert.Contains("explicit load", failure.Reason, StringComparison.Ordinal);
        Assert.Contains(expected, failure.Reason, StringComparison.Ordinal);
        Assert.Contains("'LineService'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>A navigation name two entities of the conversion declare is ambiguous where the unit states nothing about the entity in memory.</summary>
    [Fact]
    public void ANavigationTwoEntitiesDeclareIsAmbiguousWithoutAStatedType()
    {
        const string two = """
            public class Customer
            {
                public int CustomerID { get; set; }

                public List<SalesOrder> Lines { get; set; } = [];
            }

            public class SalesOrder
            {
                public int SalesOrderID { get; set; }

                public int CustomerID { get; set; }

                public List<OrderLine> Lines { get; set; } = [];
            }

            public class OrderLine
            {
                public int OrderLineID { get; set; }

                public int SalesOrderID { get; set; }
            }

            public class LineService(ShopContext ctx)
            {
                public void Fill() => ctx.Entry(Current).Collection(o => o.Lines).Load();
            }
            """;

        var result = Convert(ORMEnum.Dapper, Unit(two));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        var failure = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("more than one entity", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("Customer", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("SalesOrder", failure.Reason, StringComparison.Ordinal);
    }

    /// <summary>The members of the same API that send nothing to the database are no query, and the class around them stays an entity.</summary>
    [Fact]
    public void IsLoadedAndCurrentValueAreNoQuery()
    {
        var result = Convert(ORMEnum.Dapper, Unit(Entities + """


            public class Totals(ShopContext ctx)
            {
                public bool Known(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).IsLoaded;

                public object? Held(SalesOrder order) => ctx.Entry(order).Collection(o => o.Lines).CurrentValue;
            }
            """));

        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Equal(3, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(NotEntities(result));
    }
}
