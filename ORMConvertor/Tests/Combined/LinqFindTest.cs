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
/// <c>Find(key…)</c> and <c>FindAsync(key…)</c> on a set are the provider's look-up of one row
/// by its primary key. Until 2026-10-16 the shared LINQ parser read the call as a step the
/// representation does not carry, left it out with a loss record (decision 070) and the
/// artifact returned the whole table (decision 053) - the most common read of real EF Core
/// repositories. Now the call is expanded, before the chain is read, into the steps it is
/// short for where the mapping states the key: a Where over the key parts in their order and
/// Take(1), the shape FirstOrDefault(predicate) expands into (decision 103); a value from the
/// enclosing scope is the parameter of the same name (decision 083). What the call says
/// beyond the rows is named in a convention record. Without the key, or with another number
/// of values than the key has parts, the call is refused by name.
/// </summary>
public class LinqFindTest
{
    private static EntityMap Map(string entity, string table, string[] key, params (string Name, ScalarType Type)[] columns)
    {
        var properties = columns.Select(c => new Property { Name = c.Name, Type = LangType.Scalar(c.Type) }).ToList();
        var propertyMaps = properties.Select(p => new PropertyMap { Property = p, ColumnName = p.Name }).ToList();

        return new EntityMap
        {
            Entity = new Entity { Name = entity, Properties = properties },
            Table = table,
            Schema = "Sales",
            PropertyMaps = propertyMaps,
            PrimaryKey = key.Length == 0
                ? null
                : new PrimaryKey
                {
                    Parts = key.Select((name, i) => new PrimaryKeyPart { PropertyMap = propertyMaps.Single(p => p.Property.Name == name), Order = i + 1 }).ToList(),
                },
        };
    }

    private static EntityMap Customers(bool withKey = true) =>
        Map("Customer", "Customers", withKey ? ["CustomerID"] : [], ("CustomerID", ScalarType.Int), ("Name", ScalarType.String));

    private static EntityMap OrderLines() =>
        Map("OrderLine", "OrderLines", ["OrderID", "LineNumber"], ("OrderID", ScalarType.Int), ("LineNumber", ScalarType.Int), ("Quantity", ScalarType.Int));

    private static string Statement(string expression) =>
        $$"""
        public void Query(int id, int orderId, int lineNo, int c, CancellationToken ct)
        {
            var q = {{expression}};
        }
        """;

    private static string AsyncStatement(string expression) =>
        $$"""
        public async Task Query(int id, int orderId, int lineNo, CancellationToken ct)
        {
            var q = await {{expression}};
        }
        """;

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string source, params EntityMap[] maps)
    {
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, maps);
        return builder;
    }

    private static string Normalized(string text) => string.Join(" ", text.Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Sql(string source, params EntityMap[] maps)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), source, maps);
        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Failure);
        return Normalized(artifacts.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content);
    }

    private static ConversionRecord Refusal(string source, params EntityMap[] maps)
    {
        var builder = Parse(new DapperSqlQueryBuilder(), source, maps);

        Assert.Empty(builder.Build());
        return Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    // ---- the filter on the key with the one row taken --------------------------------------

    [Fact]
    public void FindIsTheFilterOnTheKeyWithTheOneRowTaken()
    {
        var sql = Sql(Statement("ctx.Customers.Find(id)"), Customers());

        Assert.Contains("SELECT TOP (1) * FROM Sales.Customers AS c WHERE c.CustomerID = @id", sql);
    }

    [Fact]
    public void FindAsyncSaysTheSame()
    {
        Assert.Equal(
            Sql(Statement("ctx.Customers.Find(id)"), Customers()),
            Sql(AsyncStatement("ctx.Customers.FindAsync(id)"), Customers()));
    }

    [Fact]
    public void TheArrayFormWithATokenReadsTheElementsOfTheArray()
    {
        var withToken = Sql(AsyncStatement("ctx.Customers.FindAsync(new object[] { id }, ct)"), Customers());
        var collection = Sql(AsyncStatement("ctx.Customers.FindAsync([id], ct)"), Customers());

        Assert.Contains("WHERE c.CustomerID = @id", withToken);
        Assert.Contains("WHERE c.CustomerID = @id", collection);
    }

    [Fact]
    public void ACompositeKeyTakesTheValuesInTheOrderOfItsParts()
    {
        var sql = Sql(Statement("ctx.OrderLines.Find(orderId, lineNo)"), OrderLines());

        Assert.Contains("WHERE o.OrderID = @orderId AND o.LineNumber = @lineNo", sql);
    }

    [Fact]
    public void ALiteralKeyIsALiteral()
    {
        var sql = Sql(Statement("ctx.Customers.Find(5)"), Customers());

        Assert.Contains("WHERE c.CustomerID = 5", sql);
    }

    [Fact]
    public void TheRowKeepsItsNameClearOfTheValues()
    {
        // The value is called c, the letter the row would take; the row steps aside, so that
        // the comparison is not of the row with itself.
        var sql = Sql(Statement("ctx.Customers.Find(c)"), Customers());

        Assert.Contains("FROM Sales.Customers AS c2 WHERE c2.CustomerID = @c", sql);
    }

    [Fact]
    public void TheConventionRecordNamesTheKeyAndWhatTheArtifactDoesNotCarry()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), AsyncStatement("ctx.Customers.FindAsync(id)"), Customers());
        builder.Build();

        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
        Assert.Equal(QueryFeature.Filtering, record.Feature);
        Assert.Contains("FindAsync() is carried as the filter on the key of 'Customer' ('CustomerID') with Take(1)", record.Reason);
        Assert.Contains("entities the context tracks", record.Reason);
    }

    [Fact]
    public void FindGoesOutAsEfCoreAgain()
    {
        var builder = Parse(new EFCoreLinqQueryBuilder(), Statement("ctx.Customers.Find(id)"), Customers());

        var artifacts = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        var method = Normalized(artifacts.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
        Assert.Contains("c.CustomerID == id", method);
        Assert.Contains(".Take(1)", method);
    }

    // ---- what cannot be stated is refused by name ------------------------------------------

    [Fact]
    public void WithoutTheKeyInTheMappingFindIsRefused()
    {
        var record = Refusal(Statement("ctx.Customers.Find(id)"), Customers(withKey: false));

        Assert.Equal(QueryFeature.Filtering, record.Feature);
        Assert.Contains("Find() selects the row of the primary key of 'Customer', which the mapping does not state", record.Reason);
        Assert.Contains("would return the whole table", record.Reason);
    }

    [Fact]
    public void WithoutTheMappingFindIsRefused()
    {
        var record = Refusal(Statement("ctx.Customers.Find(id)"));

        Assert.Equal(QueryFeature.Filtering, record.Feature);
        Assert.Contains("'Customers', whose entity mapping is not part of the conversion", record.Reason);
    }

    [Fact]
    public void AnotherNumberOfValuesThanTheKeyHasPartsIsRefused()
    {
        var record = Refusal(Statement("ctx.OrderLines.Find(orderId)"), OrderLines());

        Assert.Equal(QueryFeature.Filtering, record.Feature);
        Assert.Contains("Find() is handed 1 key value and the key of 'OrderLine' has 2 parts ('OrderID', 'LineNumber')", record.Reason);
    }
}
