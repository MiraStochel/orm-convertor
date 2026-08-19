using Model;
using Model.AbstractRepresentation;
using NHibernate;
using NHibernate.Hql.Ast.ANTLR;
using OrmConvertor;

namespace Tests.Verification;

/// <summary>
/// Levels 2 and 3 of decision 016 applied to the query branch, as decision 027 sets them
/// out. Everything here runs dry: no connection is configured and none is attempted.
/// </summary>
public class QueryVerificationTest
{
    private const string SourceEntity = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private const string SourceQuery = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => c.CreditLimit > 2000)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;

    private static ConversionResult Translate(ORMEnum target) =>
        ConversionHandler.Convert(
            ORMEnum.EFCore,
            target,
            [
                new() { Content = SourceEntity, ContentType = ConversionContentType.CSharpEntity },
                new() { Content = SourceQuery, ContentType = ConversionContentType.CSharpQuery },
            ]);

    private static IEnumerable<string> Entities(ConversionResult result)
        => result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content);

    private static string Query(ConversionResult result, ConversionContentType type)
        => result.Sources.First(s => s.ContentType == type).Content;

    // ---- EF Core -------------------------------------------------------------------

    /// <summary>
    /// Level 3 for EF Core: the provider translates the generated chain into SQL without a
    /// database. It is the strongest verdict available here, and it fails on anything EF Core
    /// cannot map.
    /// </summary>
    [Fact]
    public void EFCoreTranslatesTheGeneratedQuery()
    {
        var result = Translate(ORMEnum.EFCore);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "QueryVerification_EFCore_Translate",
            Query(result, ConversionContentType.CSharpQuery),
            Entities(result),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;");

        var sql = EFCoreQueryAcceptance.Translate(compiled);

        Assert.Contains("[Sales].[Customers]", sql);
        Assert.Contains("ORDER BY", sql);
    }

    /// <summary>A level that never says no proves nothing (decision 016).</summary>
    [Fact]
    public void EFCoreRefusesAnUntranslatableQuery()
    {
        var result = Translate(ORMEnum.EFCore);

        const string untranslatable = """
            public static IQueryable Query(DbContext ctx)
            {
                return ctx.Set<Customer>().Where(c => Untranslatable(c.CustomerName));
            }

            private static bool Untranslatable(string value) => value.GetHashCode() > 0;
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "QueryVerification_EFCore_Refuses",
            untranslatable,
            Entities(result),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;");

        Assert.ThrowsAny<Exception>(() => EFCoreQueryAcceptance.Translate(compiled));
    }

    // ---- NHibernate ----------------------------------------------------------------

    /// <summary>
    /// Level 3 for NHibernate: the HQL compiles against the mapped model, which also resolves
    /// every entity and property name in it (rule Q13).
    /// </summary>
    [Fact]
    public void NHibernateCompilesTheGeneratedHql()
    {
        var result = Translate(ORMEnum.NHibernate);

        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "QueryVerification_NHibernate_Compiles",
            Entities(result),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        var mappings = result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content);

        NHibernateQueryAcceptance.CompileQuery(
            compiled,
            mappings,
            Query(result, ConversionContentType.HqlQuery));
    }

    [Fact]
    public void NHibernateRefusesHqlNamingAPropertyTheEntityLacks()
    {
        var result = Translate(ORMEnum.NHibernate);

        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "QueryVerification_NHibernate_Refuses",
            Entities(result),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        var mappings = result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content).ToList();

        // An unresolvable property surfaces as QueryException; malformed HQL surfaces as its
        // subclass QuerySyntaxException. Both refusals are what makes this level worth having.
        var unresolvable = Assert.Throws<QueryException>(() =>
            NHibernateQueryAcceptance.CompileQuery(compiled, mappings, "from Customer c where c.NoSuchProperty = 1"));
        Assert.Contains("NoSuchProperty", unresolvable.Message);

        Assert.Throws<QuerySyntaxException>(() =>
            NHibernateQueryAcceptance.CompileQuery(compiled, mappings, "select from where from Customer"));
    }

    /// <summary>The generated C# wrapper has to compile too - that is level 2.</summary>
    [Fact]
    public void TheNHibernateQueryMethodCompiles()
    {
        var result = Translate(ORMEnum.NHibernate);

        GeneratedQueryCompiler.CompileOrFail(
            "QueryVerification_NHibernate_Method",
            Query(result, ConversionContentType.CSharpQuery),
            [],
            GeneratedQueryCompiler.NHibernateConsumerReferences,
            "using NHibernate;");
    }

    // ---- Dapper --------------------------------------------------------------------

    /// <summary>
    /// Level 2 for Dapper: the generated method compiles. It did not until the trailing comma
    /// in the Query&lt;T&gt; argument list was fixed, which is why AdvisorBenchmarking used to
    /// re-extract the SQL with a regex.
    /// </summary>
    [Fact]
    public void TheDapperQueryMethodCompiles()
    {
        var result = Translate(ORMEnum.Dapper);

        GeneratedQueryCompiler.CompileOrFail(
            "QueryVerification_Dapper_Method",
            Query(result, ConversionContentType.CSharpQuery),
            Entities(result),
            GeneratedQueryCompiler.DapperConsumerReferences,
            "using System.Data;\nusing Dapper;");
    }

    /// <summary>
    /// Dapper's own verdict is empty, so what is asserted instead is that the SQL parses and
    /// that every name in it resolves through the mapping IR (decision 027).
    /// </summary>
    [Fact]
    public void TheDapperSqlParsesAndResolves()
    {
        var result = Translate(ORMEnum.Dapper);
        var sql = Query(result, ConversionContentType.SqlQuery);

        var map = new EntityMap
        {
            Entity = new Entity { Name = "Customer" },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = new Property { Name = "CustomerId" }, ColumnName = "CustomerId" },
                new PropertyMap { Property = new Property { Name = "CustomerName" }, ColumnName = "CustomerName" },
                new PropertyMap { Property = new Property { Name = "CreditLimit" }, ColumnName = "CreditLimit" },
            ],
        };

        TSqlAcceptance.ResolvesAgainst(sql, [map]);
    }

    [Fact]
    public void MalformedSqlIsRefused()
    {
        Assert.ThrowsAny<Exception>(() => TSqlAcceptance.ParseOrFail("SELECT FROM WHERE ORDER"));
    }

    [Fact]
    public void SqlNamingAColumnTheEntityLacksIsRefused()
    {
        var map = new EntityMap
        {
            Entity = new Entity { Name = "Customer" },
            Table = "Customers",
            Schema = "Sales",
            PropertyMaps = [new PropertyMap { Property = new Property { Name = "CustomerId" }, ColumnName = "CustomerId" }],
        };

        Assert.ThrowsAny<Exception>(() =>
            TSqlAcceptance.ResolvesAgainst("SELECT c.NoSuchColumn FROM Sales.Customers AS c", [map]));
    }
}
