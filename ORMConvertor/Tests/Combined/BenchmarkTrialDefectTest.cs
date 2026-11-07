using System.Text.RegularExpressions;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The translation defects the trial run of the candidates of the fourth domain found over
/// empty tables (decision 208), each held over the shape of its query in the domain of the
/// query-shape matrices (<see cref="QueryShapeInputs"/>) rather than over the benchmark that
/// showed it. A Dapper source with the seven entities and one SQL text, converted without a
/// catalog into the targets the defect showed in.
/// </summary>
public class BenchmarkTrialDefectTest
{
    private static ConversionResult Convert(ORMEnum target, string sql)
        => ConversionHandler.Convert(
            ORMEnum.Dapper,
            target,
            [
                .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
                new ConversionSource { Name = "Query.sql", ContentType = ConversionContentType.SqlQuery, Content = sql.Replace("{{schema}}", QueryShapeInputs.Schema, StringComparison.Ordinal) },
            ]);

    /// <summary>The text of every query artifact, each run of whitespace one space.</summary>
    private static string Text(ConversionResult result)
        => Regex.Replace(string.Join("\n", result.Sources.Where(s => s.ContentType.IsQuery()).Select(s => s.Content)), @"\s+", " ");

    private static ConversionRecord Refused(ConversionResult result, QueryFeature feature)
    {
        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        return Assert.Single(result.Records, r => QueryShapeMatrixTest.IsQueryFailure(r) && r.Feature == feature);
    }

    private static void NoQueryRecord(ConversionResult result, params ConversionRecordKind[] kinds)
        => Assert.DoesNotContain(result.Records, r => r.Entity is null && r.Category is null && kinds.Contains(r.Kind));

    /* ---- a bare column of the query around a correlated subquery ---------------------- */

    private const string CorrelatedExists =
        "SELECT c.CustomerId FROM {{schema}}.ShopCustomers c WHERE EXISTS (SELECT * FROM {{schema}}.ShopProducts WHERE Sku = Name)";

    private const string CorrelatedScalar =
        "SELECT c.CustomerId, (SELECT MAX(ProductName) FROM {{schema}}.ShopProducts WHERE Sku = Name) AS Product FROM {{schema}}.ShopCustomers c";

    /// <summary>
    /// SQL binds a bare name to the nearest scope whose row has it: <c>Name</c> is the
    /// customer's, which the subquery's product does not map, and used to be written as the
    /// product's (CH-benCHmark Q4 and Q22, SEDE 803444).
    /// </summary>
    [Theory]
    [InlineData(CorrelatedExists)]
    [InlineData(CorrelatedScalar)]
    public void AnOuterColumnInACorrelatedSubqueryKeepsTheOuterRow(string sql)
    {
        Assert.Contains("ShopProducts.Sku = c.Name", Text(Convert(ORMEnum.Dapper, sql)));
        Assert.Contains("ShopProducts.Sku == c.Name", Text(Convert(ORMEnum.EFCore, sql)));
        Assert.Contains("ShopProducts.Sku = c.Name", Text(Convert(ORMEnum.NHibernate, sql)));
    }

    /// <summary>Two rows of one scope that both map a bare column are a name SQL Server refuses as ambiguous.</summary>
    [Fact]
    public void ABareColumnTwoRowsOfOneScopeMapIsRefused()
    {
        var result = Convert(
            ORMEnum.Dapper,
            "SELECT o.OrderId FROM {{schema}}.ShopOrders o INNER JOIN {{schema}}.ShopOrderLines l ON l.CompanyId = o.CompanyId AND l.OrderId = o.OrderId WHERE CompanyId = 1");

        Assert.Contains("ambiguous", Refused(result, QueryFeature.Filtering).Reason, StringComparison.Ordinal);
    }

    /* ---- an ordinal ordering key ------------------------------------------------------- */

    /// <summary>The position names the element of the select list, which under TOP decides which rows come back (TPC-DS 62 and 99).</summary>
    [Fact]
    public void AnOrdinalOrderingKeyNamesItsProjection()
    {
        const string sql = "SELECT TOP (2) p.Sku, p.ProductName AS Name FROM {{schema}}.ShopProducts p ORDER BY 2, 1 DESC";

        var dapper = Convert(ORMEnum.Dapper, sql);
        Assert.Contains("ORDER BY Name ASC, p.Sku DESC", Text(dapper));
        NoQueryRecord(dapper, ConversionRecordKind.Loss, ConversionRecordKind.Failure);

        Assert.Contains(".OrderBy(p => p.Name) .ThenByDescending(p => p.Sku) .Take(2)", Text(Convert(ORMEnum.EFCore, sql)));
    }

    /// <summary>A position the representation carries nothing at - out of range, the whole row - is a loss without a slice and a refusal under one.</summary>
    [Fact]
    public void AnOrdinalKeyNamingNothingUnderASliceIsRefused()
    {
        Refused(Convert(ORMEnum.Dapper, "SELECT TOP (2) p.Sku FROM {{schema}}.ShopProducts p ORDER BY 3"), QueryFeature.Ordering);

        var unsliced = Convert(ORMEnum.Dapper, "SELECT p.Sku FROM {{schema}}.ShopProducts p ORDER BY 3");
        Assert.DoesNotContain("ORDER BY", Text(unsliced), StringComparison.Ordinal);
        Assert.Single(unsliced.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Ordering);
    }

    /* ---- the * beside an expression in a derived table ------------------------------- */

    /// <summary>Over one table the * is the columns its entity maps, which the query around the derived table reads (TPC-DS 62 and 99).</summary>
    [Fact]
    public void AStarBesideAnExpressionInADerivedTableIsTheColumnsOfItsEntity()
    {
        const string sql = "SELECT sq.Short, sq.ProductId FROM (SELECT SUBSTRING(ProductName, 1, 3) AS Short, * FROM {{schema}}.ShopProducts) sq";

        var dapper = Convert(ORMEnum.Dapper, sql);
        Assert.Contains("AS Short, ShopProducts.ProductId AS ProductId, ShopProducts.ProductName AS ProductName, ShopProducts.Sku AS Sku", Text(dapper));
        NoQueryRecord(dapper, ConversionRecordKind.Loss, ConversionRecordKind.Failure);

        Assert.Contains("ProductId = ShopProducts.ProductId", Text(Convert(ORMEnum.EFCore, sql)));
    }

    /// <summary>Over several row sources no alias names the columns of the *, and the query around reads them.</summary>
    [Fact]
    public void AStarOverAJoinInADerivedTableIsRefused()
    {
        Refused(
            Convert(ORMEnum.Dapper, "SELECT sq.Short FROM (SELECT UPPER(p.Sku) AS Short, * FROM {{schema}}.ShopProducts p CROSS JOIN {{schema}}.ShopCustomers c) sq"),
            QueryFeature.Projection);
    }

    /* ---- a projection dropped under a CASE ------------------------------------------- */

    private const string RatioUnderTop =
        "SELECT TOP (100) CASE WHEN p.UnitPrice = 0 THEN NULL ELSE CAST(p.UnitPrice AS decimal(15,4)) / 2 END AS Ratio FROM {{schema}}.ShopProducts p ORDER BY Ratio";

    /// <summary>
    /// A conversion with a precision, inside a CASE as at the top: dropping the only projection
    /// leaves the whole row, and dropping the key it is ordered by under TOP other rows
    /// (TPC-DS 90).
    /// </summary>
    [Fact]
    public void AProjectionDroppedUnderACaseThatTheQueryReadsIsRefused()
    {
        var result = Convert(ORMEnum.Dapper, RatioUnderTop);

        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        Assert.Contains(result.Records, r => QueryShapeMatrixTest.IsQueryFailure(r) && r.Feature == QueryFeature.Projection);
        Assert.Contains(result.Records, r => QueryShapeMatrixTest.IsQueryFailure(r) && r.Feature == QueryFeature.Ordering && r.Reason.Contains("'Ratio'", StringComparison.Ordinal));
    }

    /// <summary>Without a slice the ordering by the dropped alias is a loss of its own, never a key naming a column that does not exist.</summary>
    [Fact]
    public void AnOrderingByADroppedAliasIsNotWrittenAsAColumn()
    {
        var result = Convert(
            ORMEnum.Dapper,
            "SELECT p.ProductId, CASE WHEN p.UnitPrice = 0 THEN NULL ELSE CAST(p.UnitPrice AS decimal(15,4)) / 2 END AS Ratio FROM {{schema}}.ShopProducts p ORDER BY Ratio");

        Assert.DoesNotContain("Ratio", Text(result), StringComparison.Ordinal);
        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Ordering && r.Reason.Contains("dropped", StringComparison.Ordinal));
    }

    /* ---- CONCAT ------------------------------------------------------------------------ */

    private const string ConcatOverANumber = "SELECT CONCAT('product/', p.ProductId) AS Link FROM {{schema}}.ShopProducts p";

    /// <summary>
    /// CONCAT converts each argument into text and takes NULL for the empty string, where +
    /// converts the text into a number and yields NULL (SEDE 1041065): the T-SQL targets write
    /// CONCAT again, the languages of the ORMs the conversion and the empty string.
    /// </summary>
    [Fact]
    public void ConcatKeepsItsConversionAndItsNull()
    {
        Assert.Contains("SELECT CONCAT('product/', p.ProductId) AS Link", Text(Convert(ORMEnum.Dapper, ConcatOverANumber)));
        Assert.Contains("Link = \"product/\" + (p.ProductId.ToString() ?? \"\")", Text(Convert(ORMEnum.EFCore, ConcatOverANumber)));
        Assert.Contains("concat('product/', coalesce(cast(p.ProductId as string), '')) as Link", Text(Convert(ORMEnum.NHibernate, ConcatOverANumber)));
        Assert.Contains("CONCAT('product/', p.ProductId)", QueryShapeMatrixTest.QueryText(Convert(ORMEnum.MyBatis, ConcatOverANumber), ORMEnum.MyBatis));
    }

    /// <summary>A + between texts stays the + it was: it yields NULL from a NULL, which CONCAT does not.</summary>
    [Fact]
    public void APlusOverTextIsNoConcat()
    {
        var sql = Text(Convert(ORMEnum.Dapper, "SELECT c.Name + c.Notes AS Label, CONCAT(c.Name, c.Notes) AS Joined FROM {{schema}}.ShopCustomers c"));

        Assert.Contains("c.Name + c.Notes AS Label, CONCAT(c.Name, c.Notes) AS Joined", sql);
    }

    /* ---- the names of the result's columns in EF Core ------------------------------- */

    /// <summary>COUNT(*) without an alias names no column, and EF Core used to write a member named * (SEDE 501829).</summary>
    [Fact]
    public void AnUnnamedAggregateBesideOtherColumnsHasNoMemberInEFCore()
    {
        const string sql = "SELECT p.Sku, COUNT(*) FROM {{schema}}.ShopProducts p GROUP BY p.Sku";

        Assert.Contains("carries no name", Refused(Convert(ORMEnum.EFCore, sql), QueryFeature.Projection).Reason, StringComparison.Ordinal);

        var nhibernate = Convert(ORMEnum.NHibernate, sql);
        Assert.Contains("select p.Sku, count(*)", Text(nhibernate));
        Assert.Contains(nhibernate.Records, r => r.Kind == ConversionRecordKind.Omitted && r.Feature == QueryFeature.Projection);
    }

    /// <summary>Two columns of one name are valid SQL and two members of one name C# refuses (SEDE 927857, TPC-DS 35).</summary>
    [Fact]
    public void TwoColumnsOfOneNameAreRefusedByEFCore()
    {
        const string sql = "SELECT c.Notes, a.Notes FROM {{schema}}.ShopCustomers c CROSS JOIN {{schema}}.ShopOrderLineAllocations a";

        Assert.Contains("Two columns of the result are named 'Notes'", Refused(Convert(ORMEnum.EFCore, sql), QueryFeature.Projection).Reason, StringComparison.Ordinal);
        Assert.Contains("select c.Notes, a.Notes", Text(Convert(ORMEnum.NHibernate, sql)));
    }

    /* ---- aliases --------------------------------------------------------------------- */

    /// <summary>T-SQL reads a string literal as an alias, and an ordering by that literal as the alias (SEDE 1159501 and 1113079).</summary>
    [Fact]
    public void AStringLiteralAliasIsADelimitedName()
    {
        const string sql = "SELECT p.Sku, COUNT(*) AS 'Number of products' FROM {{schema}}.ShopProducts p GROUP BY p.Sku ORDER BY 'Number of products' DESC";

        var dapper = Convert(ORMEnum.Dapper, sql);
        Assert.Contains("COUNT(*) AS [Number of products] FROM", Text(dapper));
        Assert.Contains("ORDER BY [Number of products] DESC", Text(dapper));
        NoQueryRecord(dapper, ConversionRecordKind.Loss, ConversionRecordKind.Failure);

        var nhibernate = Convert(ORMEnum.NHibernate, sql);
        Assert.Contains(nhibernate.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Projection);
        Assert.Contains("ORDER BY [Number of products] DESC", Text(nhibernate));
    }

    /// <summary>A literal that spells no alias is the constant SQL Server refuses in an ORDER BY, and stays outside the ordering.</summary>
    [Fact]
    public void AStringLiteralSpellingNoAliasIsNoOrderingKey()
    {
        var result = Convert(ORMEnum.Dapper, "SELECT p.Sku AS Code FROM {{schema}}.ShopProducts p ORDER BY 'Something else'");

        Assert.DoesNotContain("ORDER BY", Text(result), StringComparison.Ordinal);
    }

    /// <summary>SQL Server pairs a reference with an alias without regard to case; the target writes the alias as it was declared (SEDE 114416 and 61335).</summary>
    [Fact]
    public void AnAliasReferenceInAnotherCaseTakesTheDeclaredSpelling()
    {
        const string sql = "SELECT p.Sku, COUNT(*) AS ProductCount FROM {{schema}}.ShopProducts p GROUP BY p.Sku ORDER BY productcount DESC";

        Assert.Contains("ORDER BY ProductCount DESC", Text(Convert(ORMEnum.Dapper, sql)));
        Assert.Contains(".OrderByDescending(p => p.ProductCount)", Text(Convert(ORMEnum.EFCore, sql)));
        Assert.Contains("order by ProductCount desc", Text(Convert(ORMEnum.NHibernate, sql)));
    }

    /* ---- types ----------------------------------------------------------------------- */

    /// <summary>
    /// A moment compared with a number is refused as the arithmetic over the two is:
    /// <c>2013-01-01</c> is the number 2011 (SEDE 900754 and 803444).
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    public void AMomentComparedWithANumberIsRefused(ORMEnum target)
    {
        var record = Refused(Convert(target, "SELECT o.OrderId FROM {{schema}}.ShopOrders o WHERE o.PlacedAt > 2013-01-01"), QueryFeature.Expression);

        Assert.Contains("DateTime and Int", record.Reason, StringComparison.Ordinal);
    }

    private const string RoundOverANullable =
        "SELECT d.DepartmentId, ROUND(d.ParentDepartmentId * 1.00 / d.DepartmentId, 2) AS Ratio FROM {{schema}}.ShopDepartments d";

    /// <summary>
    /// Math.Round takes no nullable value (TPC-DS 78): rounded through its Value and nullable
    /// again, which EF Core 10.0.10 translates as the bare ROUND of the value, NULL where the
    /// value is NULL - measured here by its own query pipeline.
    /// </summary>
    [Fact]
    public void ARoundOfANullableValueCompilesAndIsTheBareRoundInEFCore()
    {
        var result = Convert(ORMEnum.EFCore, RoundOverANullable);
        Assert.Contains("Ratio = (decimal?)Math.Round((d.ParentDepartmentId * 1.00m / d.DepartmentId).Value, 2)", Text(result));

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "BenchmarkTrial_RoundOverANullable",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using System;\nusing Microsoft.EntityFrameworkCore;\nusing Shop;");

        var sql = Regex.Replace(EFCoreQueryAcceptance.Translate(compiled), @"\s+", " ");
        Assert.Contains("ROUND((CAST([s].[ParentDepartmentId] AS decimal(18,2)) * 1.0) / CAST([s].[DepartmentId] AS decimal(18,2)), 2) AS [Ratio]", sql);
        Assert.DoesNotContain("CASE", sql, StringComparison.Ordinal);
    }

    /// <summary>The lift is the writer's, not the query's: an EF Core source that writes it reads as the bare ROUND.</summary>
    [Fact]
    public void TheLiftOfARoundReadsBackAsTheRound()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [
                .. QueryShapeInputs.MappingUnits(ORMEnum.EFCore),
                new ConversionSource
                {
                    Name = "Query.cs",
                    ContentType = ConversionContentType.CSharp,
                    Content = """
                        public void Query()
                        {
                            var q = ctx.ShopDepartments
                                .Where(d => (decimal?)Math.Round((d.ParentDepartmentId * 1.00m).Value, 1) > 2)
                                .Select(d => new { DepartmentId = d.DepartmentId, Ratio = (decimal?)Math.Round((d.ParentDepartmentId * 1.00m / d.DepartmentId).Value, 2) })
                                .ToList();
                        }
                        """,
                },
            ]);

        var sql = Text(result);
        Assert.Contains("ROUND(d.ParentDepartmentId * 1.00 / d.DepartmentId, 2) AS Ratio", sql);
        Assert.Contains("WHERE ROUND(d.ParentDepartmentId * 1.00, 1) > 2", sql);
        NoQueryRecord(result, ConversionRecordKind.Loss, ConversionRecordKind.Failure);
    }

    /// <summary>SQL Server answers arithmetic over a COUNT and an int with an int, which the row of EF Core's SqlQuery declares (SEDE 529759).</summary>
    [Fact]
    public void ArithmeticOverACountIsAnIntInTheRowOfSqlQuery()
    {
        var text = Text(Convert(ORMEnum.EFCore, "SELECT COUNT(*) AS Orders, 6 AS Moderators, (COUNT(*) / 6) AS PerModerator FROM {{schema}}.ShopOrders o"));

        Assert.Contains("public int? Orders { get; set; }", text);
        Assert.Contains("public int? PerModerator { get; set; }", text);
    }

    /* ---- HQL ------------------------------------------------------------------------- */

    /// <summary>
    /// NHibernate reads the target of a join spelled like an alias in scope as a path off that
    /// alias ("Path expected for join!", CH-benCHmark Q11), and qualified with its namespace as
    /// the entity (NHibernate/NHibernateHqlProviderTest).
    /// </summary>
    [Fact]
    public void AJoinedEntitySpelledLikeAnAliasInScopeIsQualifiedInHql()
    {
        var hql = Text(Convert(
            ORMEnum.NHibernate,
            "SELECT l.ProductId, SUM(l.Quantity) AS Total FROM {{schema}}.ShopOrderLines l CROSS JOIN {{schema}}.ShopProducts ShopProduct WHERE ShopProduct.ProductId = l.ProductId GROUP BY l.ProductId "
            + "HAVING SUM(l.Quantity) > (SELECT SUM(l2.Quantity) * .005 FROM {{schema}}.ShopOrderLines l2 CROSS JOIN {{schema}}.ShopProducts p2 WHERE p2.ProductId = l2.ProductId)"));

        Assert.Contains("cross join ShopProduct ShopProduct", hql);
        Assert.Contains("cross join Shop.ShopProduct p2", hql);
    }
}
