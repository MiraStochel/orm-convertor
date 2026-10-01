using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Catalog;

/// <summary>
/// Decision 105 over a fake catalog: a query formulates a demand of its own - the tables a
/// parameter has to be typed from and no stated mapping binds - and the catalog serves it
/// between the reading of the queries and their generation, so that the scalar of a Dapper
/// source's parameter stops depending on what the <em>target</em> asked the catalog for.
///
/// The cases are the ones the decision names. The binding arrives for the two targets that
/// used to refuse it, Dapper and MyBatis, with the origin on the query and no loss on the
/// entities; without a catalog every target refuses as before, only now saying that it is
/// the catalog that is missing rather than the table; the catalog is left alone by every
/// query that needs nothing from it; a table the catalog lacks, or has twice, leaves the
/// parameter untyped with a record that says which; and the demand of several queries is
/// one read, with the origin on the query that demanded first.
/// </summary>
public class QueryDemandCompletionTest
{
    private const string OrderLineSource = """
        namespace Shop;

        public class ShopOrderLine
        {
            public int CompanyId { get; set; }
            public int OrderId { get; set; }
            public int LineNumber { get; set; }
            public int ProductId { get; set; }
            public int Quantity { get; set; }
        }
        """;

    private const string ScalarQuery = "SELECT * FROM ShopOrderLines AS ol WHERE ol.Quantity >= @minQuantity";

    private static TableImage OrderLinesImage(string schema = "dbo") => new()
    {
        Schema = schema,
        Name = "ShopOrderLines",
        Columns =
        [
            new ColumnImage { Name = "CompanyId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "OrderId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "LineNumber", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
            new ColumnImage { Name = "Quantity", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
        ],
        PrimaryKeyColumns = ["CompanyId", "OrderId", "LineNumber"],
        ForeignKeys = [],
    };

    private static ConversionSource Entity(string content = OrderLineSource, string name = "Shop.cs")
        => new() { ContentType = ConversionContentType.CSharp, Content = content, Name = name };

    private static ConversionSource Query(string sql, string name = "query.sql")
        => new() { ContentType = ConversionContentType.SqlQuery, Content = sql, Name = name };

    private static ConversionResult Convert(ORMEnum target, ICatalogReader? reader, params ConversionSource[] units)
        => ConversionHandler.Convert(ORMEnum.Dapper, target, [.. units], reader);

    private static List<ConversionSource> QueryArtifacts(ConversionResult result)
        => result.Sources.Where(s => s.ContentType.IsQuery()).ToList();

    private static IEnumerable<ConversionRecord> QueryFailures(ConversionResult result)
        => result.Records.Where(r => r.Kind == ConversionRecordKind.Failure && r.Feature == QueryFeature.QueryParameter);

    /* ---- the binding arrives, for every target alike ---------------------------------- */

    /// <summary>
    /// The heart of the decision. Neither target demands the table for its own artifact -
    /// Dapper demands nothing, MyBatis demands columns - and both used to refuse the
    /// parameter with a catalog that had the table all along. The query now asks for the
    /// binding itself, and the parameter is typed exactly as it is into EF Core.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    public void ATargetThatDemandsNoTableStillTypesTheParameterWithACatalog(ORMEnum target)
    {
        var reader = new FakeCatalogReader(OrderLinesImage());

        var result = Convert(target, reader, Entity(), Query(ScalarQuery));

        Assert.Empty(QueryFailures(result));
        // The generated method - C# for Dapper, Java for MyBatis - carries the typed signature.
        var query = Assert.Single(QueryArtifacts(result), s => s.ContentType is ConversionContentType.CSharpQuery or ConversionContentType.JavaQuery);
        Assert.Contains("int minQuantity", query.Content);

        // The origin is an event on the query that demanded the table, not a state of the
        // model, and it points back at the unit and names the fact (decisions 015 and 066).
        var supplied = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.TableName);
        Assert.Equal("ShopOrderLine", supplied.Entity);
        Assert.Equal("query.sql", supplied.Unit);
        Assert.Equal(CatalogConnectionState.Reached, result.CatalogState);
        Assert.NotNull(result.CatalogReadTime);
    }

    /// <summary>
    /// The order is part of the decision: the binding is written after the entity artifacts
    /// are out, so the mechanical loss rule has nothing to report - the source never stated
    /// a table, and nothing was lost.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    public void TheEntityArtifactsCarryNoLossAboutTheSuppliedTable(ORMEnum target)
    {
        var result = Convert(target, new FakeCatalogReader(OrderLinesImage()), Entity(), Query(ScalarQuery));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.TableName);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.SchemaName);

        // And the entity artifacts are the same ones a conversion without the query yields.
        // A MyBatis mapper document is left out on both sides: the query builder writes one
        // of its own next to the entity builder's, and the two are not told apart by type.
        static IEnumerable<string> EntityArtifacts(ConversionResult conversion)
            => conversion.Sources
                .Where(s => !s.ContentType.IsQuery() && s.ContentType != ConversionContentType.XML)
                .Select(s => s.Content);

        var without = Convert(target, new FakeCatalogReader(OrderLinesImage()), Entity());
        Assert.Equal(EntityArtifacts(without), EntityArtifacts(result));
    }

    /// <summary>
    /// The SQL target used to find the result type through the naming convention and said
    /// so; with the binding from the catalog it finds the entity through a stated mapping,
    /// the text is the same, and what the record claims changes from a guess to a fact.
    /// </summary>
    [Fact]
    public void TheConventionRecordGivesWayToTheSuppliedOne()
    {
        var result = Convert(ORMEnum.Dapper, new FakeCatalogReader(OrderLinesImage()), Entity(), Query(ScalarQuery));

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Projection);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.TableName);
        Assert.Contains("Query<ShopOrderLine>", Assert.Single(QueryArtifacts(result), s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    /* ---- without a catalog, the refusal stays and names the missing catalog ----------- */

    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    [InlineData(ORMEnum.EFCore)]
    public void WithoutACatalogEveryTargetRefusesAndSaysTheCatalogIsMissing(ORMEnum target)
    {
        var result = Convert(target, reader: null, Entity(), Query(ScalarQuery));

        Assert.Empty(QueryArtifacts(result));
        Assert.NotEmpty(QueryFailures(result));

        var reason = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.QueryParameter);
        Assert.Contains("No database connection is configured", reason.Reason);
        Assert.Contains("ShopOrderLines", reason.Reason);
        Assert.Equal("query.sql", reason.Unit);
        Assert.Equal(CatalogConnectionState.NotConfigured, result.CatalogState);
    }

    /* ---- the catalog is left alone by what needs nothing from it ---------------------- */

    public static TheoryData<string, string> QueriesThatDemandNothing() => new()
    {
        { "no query at all", "" },
        { "no parameter", "SELECT * FROM ShopOrderLines AS ol WHERE ol.Quantity >= 5" },
        { "an unqualified column", "SELECT * FROM ShopOrderLines WHERE Quantity >= @minQuantity" },
        { "a comparison with a constant", "SELECT * FROM ShopOrderLines AS ol WHERE @minQuantity >= 5" },
        { "a LIKE pattern", "SELECT * FROM ShopOrderLines AS ol WHERE ol.Description LIKE @pattern" },
        { "a row count", "SELECT * FROM ShopOrderLines AS ol ORDER BY ol.LineNumber OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY" },
        {
            "a COUNT in a subquery",
            "SELECT * FROM ShopOrderLines AS ol WHERE (SELECT COUNT(*) FROM ShopOrderLines AS x WHERE x.OrderId = ol.OrderId) >= @minLines"
        },
    };

    /// <summary>
    /// The demand is the exact set of tables the gate cannot resolve, so a query typed from
    /// anything else never touches the catalog - a Dapper target's run stays at Unused, as
    /// an empty target demand leaves it (decision 015). A LIKE pattern is a string whichever
    /// column it matches, a constant types the parameter itself, a row count is Int by its
    /// clause, and an unqualified column is looked up across every entity of the conversion.
    /// </summary>
    [Theory]
    [MemberData(nameof(QueriesThatDemandNothing))]
    public void AQueryThatNeedsNoBindingLeavesTheCatalogUnused(string _, string sql)
    {
        var reader = new FakeCatalogReader(OrderLinesImage());
        var units = sql.Length == 0 ? new[] { Entity() } : [Entity(), Query(sql)];

        var result = Convert(ORMEnum.Dapper, reader, units);

        Assert.Equal(0, reader.Reads);
        Assert.Equal(CatalogConnectionState.Unused, result.CatalogState);
        Assert.Null(result.CatalogReadTime);
    }

    /// <summary>The demand is data on the template, and a query without parameters yields none of it.</summary>
    [Fact]
    public void TheDemandIsEmptyWhereTheMappingBindsTheTable()
    {
        // An EF Core source states the table on the entity, so the gate resolves the alias
        // and nothing is demanded - even though the query is the same shape.
        const string efCoreEntity = """
            using System.ComponentModel.DataAnnotations.Schema;

            namespace Shop;

            [Table("ShopOrderLines")]
            public class ShopOrderLine
            {
                public int CompanyId { get; set; }
                public int OrderId { get; set; }
                public int LineNumber { get; set; }
                public int ProductId { get; set; }
                public int Quantity { get; set; }
            }
            """;

        var reader = new FakeCatalogReader(OrderLinesImage());
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                new ConversionSource { ContentType = ConversionContentType.CSharp, Content = efCoreEntity },
                new ConversionSource
                {
                    ContentType = ConversionContentType.CSharp,
                    Content = "public void Query() { var q = ctx.ShopOrderLines.Where(ol => ol.Quantity >= minQuantity).ToList(); }",
                },
            ],
            reader);

        Assert.Empty(QueryFailures(result));
        Assert.Equal(0, reader.Reads);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Supplied);
    }

    /// <summary>
    /// A parameter compared with a subquery takes the scalar of the value the subquery
    /// projects, so the table under that value is demanded - in the subquery's own scope,
    /// where its alias is declared - and without a catalog the refusal names it.
    /// </summary>
    [Fact]
    public void ASubqueryDemandsTheTableOfTheValueItProjects()
    {
        const string sql = """
            SELECT * FROM ShopOrderLines AS ol
            WHERE @minQuantity <= (SELECT MAX(x.Quantity) FROM ShopOrderLines AS x WHERE x.OrderId = ol.OrderId)
            """;

        var reader = new FakeCatalogReader(OrderLinesImage());
        var result = Convert(ORMEnum.Dapper, reader, Entity(), Query(sql));

        Assert.Empty(QueryFailures(result));
        Assert.Equal(1, reader.Reads);
        Assert.Contains("int minQuantity", Assert.Single(QueryArtifacts(result), s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.TableName && r.Entity == "ShopOrderLine");

        var without = Convert(ORMEnum.Dapper, reader: null, Entity(), Query(sql));

        Assert.Empty(QueryArtifacts(without));
        var reason = Assert.Single(without.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.QueryParameter);
        Assert.Contains("ShopOrderLines", reason.Reason);
    }

    /* ---- a table the catalog lacks, or has twice ------------------------------------ */

    [Fact]
    public void ATableTheCatalogDoesNotHaveLeavesTheParameterUntypedAndSaysSo()
    {
        var reader = new FakeCatalogReader();

        var result = Convert(ORMEnum.Dapper, reader, Entity(), Query(ScalarQuery));

        Assert.Empty(QueryArtifacts(result));
        Assert.NotEmpty(QueryFailures(result));
        var reason = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.QueryParameter);
        Assert.Contains("No table matching 'ShopOrderLines' was found in the catalog", reason.Reason);
        Assert.Equal(1, reader.Reads);
        Assert.Equal(CatalogConnectionState.Reached, result.CatalogState);
    }

    /// <summary>
    /// The case from the history of decision 089: the same table in two schemas. Without a
    /// schema in the query the catalog cannot say which one, and it does not guess; with
    /// the schema in the query it binds the one the query names.
    /// </summary>
    [Fact]
    public void TheSameTableInTwoSchemasBindsOnlyWhereTheQueryNamesTheSchema()
    {
        var reader = new FakeCatalogReader(OrderLinesImage("sales"), OrderLinesImage("archive"));

        var unqualified = Convert(ORMEnum.Dapper, reader, Entity(), Query(ScalarQuery));

        Assert.Empty(QueryArtifacts(unqualified));
        var reason = Assert.Single(unqualified.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.QueryParameter);
        Assert.Contains("More than one table in the catalog matches", reason.Reason);

        var qualified = Convert(ORMEnum.Dapper, reader, Entity(),
            Query("SELECT * FROM archive.ShopOrderLines AS ol WHERE ol.Quantity >= @minQuantity"));

        Assert.Empty(QueryFailures(qualified));
        Assert.Contains("int minQuantity", Assert.Single(QueryArtifacts(qualified), s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains(qualified.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.SchemaName && r.Reason.Contains("'archive'"));
    }

    /// <summary>
    /// A table no entity of the conversion could be is not asked about: the catalog cannot
    /// bind it to anything, so a read would answer a question nobody can use.
    /// </summary>
    [Fact]
    public void ATableNoEntityCouldBeIsNotAskedAbout()
    {
        var reader = new FakeCatalogReader(OrderLinesImage());

        var result = Convert(ORMEnum.Dapper, reader, Entity(),
            Query("SELECT * FROM Invoices AS i WHERE i.Total >= @minTotal"));

        Assert.Empty(QueryArtifacts(result));
        Assert.Equal(0, reader.Reads);
        var reason = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Incompleteness && r.Feature == QueryFeature.QueryParameter);
        Assert.Contains("No entity of the conversion is named so that the naming rule would put it in the table 'Invoices'", reason.Reason);
    }

    /* ---- several queries, one read ----------------------------------------------------- */

    /// <summary>
    /// The demand of every query goes to the catalog in one batch - one read for the whole
    /// request, however many queries name the table - and the origin is on the query that
    /// demanded it first, which the order of the input decides (S2). The second query finds
    /// the binding as a stated mapping and asks for nothing.
    /// </summary>
    [Fact]
    public void TwoQueriesOverTheSameTableReadTheCatalogOnceAndTheFirstCarriesTheOrigin()
    {
        var reader = new FakeCatalogReader(OrderLinesImage());

        var result = Convert(ORMEnum.Dapper, reader,
            Entity(),
            Query(ScalarQuery, "first.sql"),
            Query("SELECT * FROM ShopOrderLines AS ol WHERE ol.ProductId IN (@ids)", "second.sql"));

        Assert.Equal(1, reader.Reads);
        Assert.Empty(QueryFailures(result));
        Assert.Equal(2, QueryArtifacts(result).Count(s => s.ContentType == ConversionContentType.SqlQuery));

        var supplied = result.Records.Where(r => r.Kind == ConversionRecordKind.Supplied).ToList();
        Assert.NotEmpty(supplied);
        Assert.All(supplied, r => Assert.Equal("first.sql", r.Unit));
    }

    /// <summary>
    /// Two different tables of two queries are still one read: the batch is the request's,
    /// not the query's.
    /// </summary>
    [Fact]
    public void TwoQueriesOverTwoTablesAreStillOneRead()
    {
        const string products = """
            namespace Shop;

            public class ShopProduct
            {
                public int ProductId { get; set; }
                public decimal UnitPrice { get; set; }
            }
            """;

        var productsImage = new TableImage
        {
            Schema = "dbo",
            Name = "ShopProducts",
            Columns =
            [
                new ColumnImage { Name = "ProductId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = false },
                new ColumnImage { Name = "UnitPrice", Type = DatabaseType.Decimal, Precision = 18, Scale = 2, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["ProductId"],
            ForeignKeys = [],
        };

        var reader = new FakeCatalogReader(OrderLinesImage(), productsImage);

        var result = Convert(ORMEnum.Dapper, reader,
            Entity(), Entity(products, "Products.cs"),
            Query(ScalarQuery, "lines.sql"),
            Query("SELECT * FROM ShopProducts AS p WHERE p.UnitPrice >= @minPrice", "products.sql"));

        Assert.Equal(1, reader.Reads);
        Assert.Empty(QueryFailures(result));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Entity == "ShopOrderLine" && r.Unit == "lines.sql");
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Entity == "ShopProduct" && r.Unit == "products.sql");
    }

    /* ---- the categories of the matrix ------------------------------------------------- */

    /// <summary>
    /// The finding the decision came from: the parametric categories of T2 a Dapper source
    /// states, into the two targets that used to refuse them, with a catalog that has the
    /// domain. This is what puts Dapper back as a source of those categories in the
    /// differential matrix (decision 089). The collection parameter is not among them: a
    /// bare Dapper unit cannot state it, which the manifest says above its section.
    /// </summary>
    [Theory]
    [InlineData("ScalarParameter", ORMEnum.Dapper)]
    [InlineData("ScalarParameter", ORMEnum.MyBatis)]
    [InlineData("InListWithABoundValue", ORMEnum.Dapper)]
    [InlineData("InListWithABoundValue", ORMEnum.MyBatis)]
    public void AParametricCategoryFromDapperIsTypedIntoTheSqlTargetsWithACatalog(string category, ORMEnum target)
    {
        var shape = QueryShapeInputs.Categories.Single(s => s.Name == category);
        var reader = new FakeCatalogReader(OrderLinesImage(QueryShapeInputs.Schema));

        var result = ConversionHandler.Convert(ORMEnum.Dapper, target, QueryShapeInputs.Units(ORMEnum.Dapper, shape), reader);

        Assert.Empty(QueryFailures(result));
        Assert.NotEmpty(QueryArtifacts(result));
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Supplied && r.Category == MappingFactCategory.TableName && r.Entity == "ShopOrderLine");

        // One read for the queries' demand, on top of the one the target's own demand
        // makes where it makes one - MyBatis asks for columns, Dapper for nothing.
        Assert.Equal(target == ORMEnum.Dapper ? 1 : 2, reader.Reads);
    }
}
