using Model;
using Model.AbstractRepresentation;
using OrmConvertor;
using Tests.Combined;

namespace Tests.Verification;

/// <summary>
/// Levels 2 and 3 of decision 016 over the deliberately bad query of
/// <see cref="QueryShapeInputs.DeeplyNested"/>, from every source into every .NET target,
/// as decision 027 sets them out for the query branch: the generated method compiles beside
/// the generated entities, EF Core translates the chain to SQL through its own provider,
/// NHibernate compiles the HQL against the generated mapping, and the T-SQL of the two SQL
/// targets parses and resolves every name through the mapping. Everything here runs dry.
/// </summary>
public class DeeplyNestedQueryVerificationTest
{
    public static TheoryData<ORMEnum> Sources() => DeeplyNestedQueryTest.Sources();

    /// <summary>The sources that state a key: the ones whose entities an NHibernate mapping can be built from.</summary>
    public static TheoryData<ORMEnum> KeyStatingSources()
    {
        var data = new TheoryData<ORMEnum>();
        foreach (var source in new[] { ORMEnum.EFCore, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink })
        {
            data.Add(source);
        }

        return data;
    }

    private static IEnumerable<string> Entities(ConversionResult result)
        => result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content);

    private static string Query(ConversionResult result, ConversionContentType type)
        => result.Sources.Single(s => s.ContentType == type).Content;

    // ---- EF Core -------------------------------------------------------------------

    /// <summary>
    /// Level 3 for EF Core: the provider translates the generated chain - three joins, a
    /// grouping over a transparent tuple, eight nested subqueries and a bound slice - into
    /// SQL without a database. It fails on anything EF Core cannot map, and a keyless entity
    /// (the two sources stating no key come out as such) is mapped like any other.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void EFCoreTranslatesTheDeeplyNestedQuery(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.EFCore);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            $"DeeplyNested_EFCore_From{source}",
            Query(result, ConversionContentType.CSharpQuery),
            Entities(result),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using System;\nusing Microsoft.EntityFrameworkCore;\nusing Shop;");

        var sql = EFCoreQueryAcceptance.Translate(compiled);

        // No DISTINCT among these on purpose: the provider drops a Distinct() inside the
        // receiver of Contains, because IN does not care about duplicates - a rewrite that
        // keeps the row set, and one this level is entitled to make.
        Assert.Contains("INNER JOIN", sql);
        Assert.Contains("LEFT JOIN", sql);
        Assert.Contains("GROUP BY", sql);
        Assert.Contains("HAVING", sql);
        Assert.Contains("EXISTS", sql);
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("ORDER BY", sql);

        if (DeeplyNestedQueryTest.StatesPagination(source))
        {
            Assert.Contains("OFFSET", sql);
        }
    }

    // ---- NHibernate ----------------------------------------------------------------

    /// <summary>
    /// Level 3 for NHibernate: the HQL compiles against the generated mapping, which resolves
    /// every entity and property of the five classes and every alias of the nine scopes
    /// (rule Q13), and accepts the entity joins over two and three columns.
    /// </summary>
    [Theory]
    [MemberData(nameof(KeyStatingSources))]
    public void NHibernateCompilesTheDeeplyNestedHql(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.NHibernate);

        var compiled = GeneratedEntityCompiler.CompileOrFail(
            $"DeeplyNested_NHibernate_From{source}",
            Entities(result),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        var mappings = result.Sources
            .Where(s => s.ContentType == ConversionContentType.XML)
            .Select(s => s.Content)
            .ToList();

        NHibernateQueryAcceptance.CompileQuery(compiled, mappings, Query(result, ConversionContentType.HqlQuery));
    }

    /// <summary>Level 2 for NHibernate: the method with its bindings and, where sent, its slice compiles.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheNHibernateQueryMethodCompiles(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.NHibernate);

        GeneratedQueryCompiler.CompileOrFail(
            $"DeeplyNested_NHibernate_Method_From{source}",
            Query(result, ConversionContentType.CSharpQuery),
            [],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using System;\nusing NHibernate;");
    }

    // ---- Dapper and MyBatis --------------------------------------------------------

    /// <summary>Level 2 for Dapper: the method with its anonymous parameter object compiles beside the entities.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheDapperQueryMethodCompiles(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.Dapper);

        GeneratedQueryCompiler.CompileOrFail(
            $"DeeplyNested_Dapper_Method_From{source}",
            Query(result, ConversionContentType.CSharpQuery),
            Entities(result),
            GeneratedQueryCompiler.DapperConsumerReferences,
            "using System;\nusing System.Data;\nusing Dapper;\nusing Shop;");
    }

    /// <summary>
    /// Dapper's own verdict is empty, so what is asserted is that the SQL parses and that
    /// every table and every qualified column of the nine scopes resolves through the mapping
    /// (decision 027) - the aliases of the nested scopes included.
    /// </summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheDapperSqlParsesAndResolves(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.Dapper);

        TSqlAcceptance.ResolvesAgainst(Query(result, ConversionContentType.SqlQuery), Maps(source));
    }

    /// <summary>The same verdict over the statement inside the MyBatis mapper, placeholders read as variables.</summary>
    [Theory]
    [MemberData(nameof(Sources))]
    public void TheMyBatisStatementParsesAndResolves(ORMEnum source)
    {
        var result = DeeplyNestedQueryTest.Convert(source, ORMEnum.MyBatis);

        foreach (var sql in QueryShapeMatrixTest.EmittedSql(result, ORMEnum.MyBatis))
        {
            TSqlAcceptance.ResolvesAgainst(sql, Maps(source));
        }
    }

    /// <summary>The five entities as every source of the domain names their tables (see the inputs).</summary>
    private static List<EntityMap> Maps(ORMEnum source) =>
    [
        Map("ShopCustomer", "ShopCustomers", "CustomerId", "Name", "Notes"),
        Map("ShopOrder", QueryShapeInputs.OrdersTable, "CompanyId", "OrderId", "CustomerId", "PlacedAt", "IsCancelled"),
        Map("ShopOrderLine", "ShopOrderLines", "CompanyId", "OrderId", "LineNumber", "ProductId", "Description", "Quantity", "UnitPrice"),
        Map("ShopOrderLineAllocation", "ShopOrderLineAllocations", "CompanyId", "OrderId", "LineNumber", "AllocationId", "AllocatedQuantity", "Notes"),
        Map("ShopProduct", "ShopProducts", "ProductId", "ProductName", "Sku", "UnitPrice", "IsDiscontinued"),
    ];

    private static EntityMap Map(string entity, string table, params string[] columns) => new()
    {
        Entity = new Entity { Name = entity },
        Table = table,
        Schema = QueryShapeInputs.Schema,
        PropertyMaps = columns
            .Select(column => new PropertyMap { Property = new Property { Name = column }, ColumnName = column })
            .ToList(),
    };
}
