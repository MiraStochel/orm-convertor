using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Tests.Combined;

/// <summary>
/// A member a LINQ query names is the column its mapping states for the property - the way
/// the JPQL reader resolves an attribute and the T-SQL reader reads a column. Carried as the
/// member, a property over a renamed column (<c>[Column("ShipToCity")] City</c>) became a
/// column no table has in every SQL target, and a parameter compared with it found no scalar
/// to take, so the query was refused in every target (decision 083). Every visitor over
/// entities writes the column back as its property, so the LINQ and JPQL targets keep the
/// member.
/// </summary>
public class LinqColumnNameTest
{
    private static EntityMap Map(string entity, string table, params (string Property, string Column, ScalarType Type, bool Nullable)[] columns)
    {
        var properties = columns.Select(c => new Property { Name = c.Property, Type = LangType.Scalar(c.Type, isNullable: c.Nullable) }).ToList();

        return new EntityMap
        {
            Entity = new Entity { Name = entity, Properties = properties },
            Table = table,
            Schema = "Ordering",
            PropertyMaps = [.. properties.Zip(columns, (property, column) => new PropertyMap { Property = property, ColumnName = column.Column })],
        };
    }

    private static EntityMap[] Maps() =>
    [
        Map("SalesOrder", "SalesOrders",
            ("SalesOrderId", "SalesOrderId", ScalarType.Int, false),
            ("City", "ShipToCity", ScalarType.String, true),
            ("CustomerId", "CustomerRef", ScalarType.Int, false)),
        Map("Customer", "Customers",
            ("CustomerId", "CustomerId", ScalarType.Int, false),
            ("Name", "FullName", ScalarType.String, false)),
    ];

    private static AbstractQueryBuilder Parse(AbstractQueryBuilder builder, string method)
    {
        var maps = Maps();
        builder.EntityMaps = maps;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, method, maps);
        return builder;
    }

    private static string Artifact(AbstractQueryBuilder builder, ConversionContentType type)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Loss);
        return built.Single(s => s.ContentType == type).Content;
    }

    private static string Sql(string method) => Artifact(Parse(new DapperSqlQueryBuilder(), method), ConversionContentType.SqlQuery);

    private static string Query(string chain) =>
        $$"""
        public void Query(string city)
        {
            var q = {{chain}}.ToList();
        }
        """;

    [Fact]
    public void AFilterOverARenamedPropertyWritesTheColumn()
    {
        Assert.Contains("WHERE o.ShipToCity = 'Brno'", Sql(Query("ctx.SalesOrders.Where(o => o.City == \"Brno\")")), StringComparison.Ordinal);
    }

    /// <summary>The parameter takes its scalar from the column it is compared with, as it does from a column of its own name.</summary>
    [Fact]
    public void AParameterComparedWithARenamedPropertyIsTypedByIt()
    {
        var builder = Parse(new DapperSqlQueryBuilder(), Query("ctx.SalesOrders.Where(o => o.City == city)"));
        var built = builder.Build();

        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("WHERE o.ShipToCity = @city", built.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content, StringComparison.Ordinal);
        Assert.Matches(@"string\??\s+city", built.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content);
    }

    [Fact]
    public void AProjectionOfARenamedPropertyWritesTheColumnUnderTheMemberName()
    {
        Assert.Contains(
            "SELECT o.ShipToCity AS City, o.SalesOrderId AS SalesOrderId",
            Sql(Query("ctx.SalesOrders.Select(o => new { o.City, o.SalesOrderId })")),
            StringComparison.Ordinal);
    }

    /// <summary>Both key selectors of a join and a column reached through the joined row name their columns - the joined table's before its row is part of the scope.</summary>
    [Fact]
    public void AJoinOverRenamedPropertiesWritesTheColumns()
    {
        var sql = Sql(Query(
            "ctx.SalesOrders.Join(ctx.Customers, o => o.CustomerId, c => c.CustomerId, (o, c) => new { o, c }).Select(x => new { x.c.Name, x.o.City })"));

        Assert.Contains("ON o.CustomerRef = c.CustomerId", sql, StringComparison.Ordinal);
        Assert.Contains("c.FullName AS Name", sql, StringComparison.Ordinal);
        Assert.Contains("o.ShipToCity AS City", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarSubqueryOverARenamedPropertyWritesTheColumn()
    {
        var sql = Sql(Query("ctx.Customers.Where(c => c.CustomerId < ctx.SalesOrders.Max(o => o.CustomerId))"));

        Assert.Matches(@"MAX\(\w+\.CustomerRef\)", sql);
    }

    /// <summary>The targets over entities write the column back as its property, so a LINQ source keeps its members into LINQ and JPQL.</summary>
    [Fact]
    public void TheTargetsOverEntitiesWriteThePropertyBack()
    {
        const string chain = "ctx.SalesOrders.Where(o => o.City == city).OrderBy(o => o.CustomerId)";

        var linq = Artifact(Parse(new EFCoreLinqQueryBuilder(), Query(chain)), ConversionContentType.CSharpQuery);
        Assert.Contains("o.City == city", linq, StringComparison.Ordinal);
        Assert.Contains("OrderBy(o => o.CustomerId)", linq, StringComparison.Ordinal);

        var jpql = Artifact(Parse(new HibernateJpqlQueryBuilder(), Query(chain)), ConversionContentType.JpqlQuery);
        Assert.Contains("o.City = :city", jpql, StringComparison.Ordinal);
        Assert.Contains("order by o.CustomerId", jpql, StringComparison.Ordinal);
    }
}
