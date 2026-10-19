using System.Collections;
using System.Globalization;
using System.Reflection;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Database;
using Tests.Differential;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// A LINQ predicate in SQL's three-valued logic (decision 156), at the fourth level over the
/// fixture of the differential matrix, where product 2 has no weight, the departments 1 and
/// 7 have no parent and three customers have no notes. Written: a condition of a SQL source
/// selects in EF Core exactly the rows SQL Server selects for the source itself - over
/// <c>&lt;&gt;</c>, NOT, NOT IN, NOT LIKE and the equality of two values that may be NULL,
/// where EF Core's own compensation for NULL used to keep the rows SQL drops. Read: a
/// predicate of an EF Core source becomes the SQL that selects the rows EF Core selects for
/// that very predicate, its Sum included (decision 155).
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class LinqNullSemanticsTest(TestSchemaFixture fixture)
{
    // ---- written: a SQL source into EF Core ------------------------------------------------

    private const string DapperShop = """
        namespace Shop;

        public class ShopCustomer
        {
            public int CustomerId { get; set; }
            public string Name { get; set; }
            public string Notes { get; set; }
        }

        public class ShopDepartment
        {
            public int DepartmentId { get; set; }
            public int? ParentDepartmentId { get; set; }
            public string Name { get; set; }
        }

        public class ShopProduct
        {
            public int ProductId { get; set; }
            public string ProductName { get; set; }
        }

        public class ShopProductLink
        {
            public int LinkId { get; set; }
            public int FromProductId { get; set; }
            public int ToProductId { get; set; }
        }
        """;

    public static TheoryData<string, string, string[]> Written => new()
    {
        { "inequality", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d WHERE d.ParentDepartmentId <> 1", ["3", "4", "6", "8", "9"] },
        { "negated comparison", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d WHERE NOT (d.ParentDepartmentId > 2)", ["2", "3", "5"] },
        { "NOT IN", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d WHERE d.ParentDepartmentId NOT IN (1, 8)", ["3", "4", "6", "8"] },
        { "equality of two nullable values", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d INNER JOIN {{schema}}.ShopDepartments AS e ON e.DepartmentId = d.DepartmentId WHERE d.ParentDepartmentId = e.ParentDepartmentId", ["2", "3", "4", "5", "6", "8", "9"] },
        { "NOT LIKE", "SELECT c.CustomerId AS CustomerId FROM {{schema}}.ShopCustomers AS c WHERE c.Notes NOT LIKE 'A%'", ["2"] },
        { "NOT IN over a subquery with a NULL", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d WHERE d.DepartmentId NOT IN (SELECT e.ParentDepartmentId FROM {{schema}}.ShopDepartments AS e)", [] },
        { "NOT IN over a subquery without one", "SELECT d.DepartmentId AS DepartmentId FROM {{schema}}.ShopDepartments AS d WHERE d.DepartmentId NOT IN (SELECT e.ParentDepartmentId FROM {{schema}}.ShopDepartments AS e WHERE e.DepartmentId IN (2, 3, 4))", ["4", "5", "6", "7", "8", "9"] },
        { "inequality on the optional side of a left join", "SELECT p.ProductId AS ProductId FROM {{schema}}.ShopProducts AS p LEFT JOIN {{schema}}.ShopProductLinks AS l ON l.FromProductId = p.ProductId WHERE l.ToProductId <> 5", ["1", "2", "3", "3"] },
    };

    [Theory]
    [MemberData(nameof(Written))]
    public void AConditionOfASqlSourceSelectsInEFCoreTheRowsSqlSelects(string shape, string sql, string[] rows)
    {
        fixture.SkipIfUnavailable();

        var expected = Direct(sql);
        Assert.Equal(rows, expected);

        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.EFCore,
            [
                new() { Content = DapperShop, ContentType = ConversionContentType.CSharp },
                new() { Content = sql.Replace("{{schema}}", fixture.SchemaName), ContentType = ConversionContentType.SqlQuery },
            ],
            fixture.CatalogReader);

        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Equal(expected, Run(Compile(
            method,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            "LinqNullSemantics_Written_" + string.Concat(shape.Where(char.IsLetterOrDigit)))));
    }

    // ---- read: an EF Core source into SQL ---------------------------------------------------

    public static TheoryData<string, string, string[]> Read => new()
    {
        { "inequality", "ctx.Set<ShopDepartment>().Where(d => d.ParentDepartmentId != 1).Select(d => new { d.DepartmentId })", ["1", "3", "4", "6", "7", "8", "9"] },
        { "negated comparison", "ctx.Set<ShopDepartment>().Where(d => !(d.ParentDepartmentId > 2)).Select(d => new { d.DepartmentId })", ["1", "2", "3", "5", "7"] },
        { "negated IN", "ctx.Set<ShopDepartment>().Where(d => !new int?[] { 1, 8 }.Contains(d.ParentDepartmentId)).Select(d => new { d.DepartmentId })", ["1", "3", "4", "6", "7", "8"] },
        { "negated LIKE", "ctx.Set<ShopCustomer>().Where(c => !c.Notes.StartsWith(\"A\")).Select(c => new { c.CustomerId })", ["0", "1", "2", "3"] },
        { "equality with a subquery that is NULL", "ctx.Set<ShopDepartment>().Where(d => d.ParentDepartmentId == ctx.Set<ShopDepartment>().Where(e => e.DepartmentId == 1).Max(e => e.ParentDepartmentId)).Select(d => new { d.DepartmentId })", ["1", "7"] },
        { "Contains over a subquery with a NULL", "ctx.Set<ShopDepartment>().Where(d => ctx.Set<ShopDepartment>().Select(e => e.ParentDepartmentId).Contains(d.ParentDepartmentId)).Select(d => new { d.DepartmentId })", ["1", "2", "3", "4", "5", "6", "7", "8", "9"] },
        { "negated Contains over a subquery with a NULL", "ctx.Set<ShopDepartment>().Where(d => !ctx.Set<ShopDepartment>().Select(e => e.ParentDepartmentId).Contains(d.DepartmentId)).Select(d => new { d.DepartmentId })", ["4", "6", "9"] },
        { "Sum over a correlated subquery without rows", "ctx.Set<ShopProduct>().Where(p => p.ProductId >= ctx.Set<ShopProductLink>().Where(l => l.FromProductId == p.ProductId).Sum(l => l.LinkId)).Select(p => new { p.ProductId })", ["1", "2", "5", "6"] },
        { "Sum over a group without a value", "ctx.Set<ShopDepartment>().GroupBy(d => d.DepartmentId).Select(g => new { DepartmentId = g.Key, Parents = g.Sum(d => d.ParentDepartmentId) })", ["1|0", "2|1", "3|2", "4|3", "5|1", "6|5", "7|0", "8|7", "9|8"] },
    };

    [Theory]
    [MemberData(nameof(Read))]
    public void APredicateOfAnEFCoreSourceReadsAsTheSqlThatSelectsItsRows(string shape, string chain, string[] rows)
    {
        fixture.SkipIfUnavailable();

        var entities = QueryShapeInputs.MappingUnits(ORMEnum.EFCore).Single().Content;
        var assembly = "LinqNullSemantics_Read_" + string.Concat(shape.Where(char.IsLetterOrDigit));

        // The rows EF Core itself selects for the predicate, the reference.
        var own = Run(Compile($"public static IQueryable Query(DbContext ctx) {{ return {chain}; }}", [entities], assembly));
        Assert.Equal(rows, own);

        var unit = $$"""
            {{entities}}

            public class Queries(DbContext ctx)
            {
                public object Query() => {{chain}}.ToList();
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [new() { Content = unit, ContentType = ConversionContentType.CSharp, Name = "Queries.cs" }]);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        var sql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;
        Assert.Equal(own, Direct(sql));
    }

    /// <summary>
    /// The HQL the shapes of the reading become is HQL that NHibernate 5.7.0 compiles: COALESCE
    /// over a scalar subquery and over an aggregate of the group, a scalar subquery tested for
    /// NULL (the third level, its query plan; JPQL in <c>NullTestedSubqueryClaimsTest</c> of the
    /// Java suite).
    /// </summary>
    [Theory]
    [InlineData("ctx.Set<ShopProduct>().Where(p => p.ProductId >= ctx.Set<ShopProductLink>().Where(l => l.FromProductId == p.ProductId).Sum(l => l.LinkId)).Select(p => new { p.ProductId })", "coalesce((select sum(")]
    [InlineData("ctx.Set<ShopDepartment>().GroupBy(d => d.DepartmentId).Select(g => new { DepartmentId = g.Key, Parents = g.Sum(d => d.ParentDepartmentId) })", "coalesce(sum(")]
    [InlineData("ctx.Set<ShopDepartment>().Where(d => d.ParentDepartmentId == ctx.Set<ShopDepartment>().Where(e => e.DepartmentId == 1).Max(e => e.ParentDepartmentId)).Select(d => new { d.DepartmentId })", ") is null")]
    public void TheReadingCompilesInNHibernate(string chain, string hallmark)
    {
        var unit = $$"""
            {{QueryShapeInputs.MappingUnits(ORMEnum.EFCore).Single().Content}}

            public class Queries(DbContext ctx)
            {
                public object Query() => {{chain}}.ToList();
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, [new() { Content = unit, ContentType = ConversionContentType.CSharp, Name = "Queries.cs" }]);
        Assert.DoesNotContain(result.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
        var hql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.HqlQuery).Content;
        Assert.Contains(hallmark, hql, StringComparison.Ordinal);

        var compiled = GeneratedEntityCompiler.CompileOrFail(
            "LinqNullSemantics_Hql_" + string.Concat(hallmark.Where(char.IsLetter)),
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedEntityCompiler.NHibernateConsumerReferences);

        Assert.NotEmpty(NHibernateQueryAcceptance.Sql(compiled, result.Sources.Where(s => s.ContentType == ConversionContentType.XML).Select(s => s.Content), hql));
    }

    /// <summary>What the representation does not carry exactly is refused by name, not read as IN (decision 070).</summary>
    [Theory]
    [InlineData("ctx.Set<ShopDepartment>().Where(d => ctx.Set<ShopDepartment>().Select(e => e.ParentDepartmentId).Any(v => v == d.ParentDepartmentId))")]
    [InlineData("ctx.Set<ShopDepartment>().Where(d => !ctx.Set<ShopDepartment>().Select(e => e.ParentDepartmentId).All(v => d.ParentDepartmentId > v))")]
    public void WhatTheRepresentationDoesNotCarryIsRefused(string chain)
    {
        var unit = $$"""
            {{QueryShapeInputs.MappingUnits(ORMEnum.EFCore).Single().Content}}

            public class Queries(DbContext ctx)
            {
                public object Query() => {{chain}}.ToList();
            }
            """;

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, [new() { Content = unit, ContentType = ConversionContentType.CSharp, Name = "Queries.cs" }]);

        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("NULL", StringComparison.Ordinal));
    }

    // ---- the run ----------------------------------------------------------------------------

    private static byte[] Compile(string method, IEnumerable<string> entities, string assembly)
        => GeneratedQueryCompiler.CompileOrFail(
            assembly,
            method,
            entities,
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Shop;");

    /// <summary>The rows the compiled query returns, each its values joined, sorted.</summary>
    private List<string> Run(byte[] compiled)
    {
        var loaded = Assembly.Load(compiled);
        using var connection = fixture.OpenConnection();
        using var context = EFCoreAcceptance.OpenContext(loaded, connection);

        var query = loaded.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(candidate => candidate.DeclaringType?.Name == "GeneratedQueries");

        var rows = new List<string>();
        foreach (var row in (IEnumerable)query.Invoke(null, [context])!)
        {
            rows.Add(string.Join("|", row.GetType().GetProperties().Select(property => Format(property.GetValue(row)))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    /// <summary>The rows SQL Server returns for the text itself.</summary>
    private List<string> Direct(string sql)
    {
        using var connection = fixture.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql.Replace("{{schema}}", fixture.SchemaName);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Format(reader.IsDBNull(i) ? null : reader.GetValue(i)))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static string Format(object? value) => value is null ? "NULL" : Convert.ToString(value, CultureInfo.InvariantCulture)!;
}
