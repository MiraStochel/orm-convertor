using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EclipseLinkWrappers;
using EFCoreWrappers;
using HibernateWrappers;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using MyBatisWrappers;
using NHibernateWrappers;
using Tests.Database;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The four extensions of the expression vocabulary of decision 113: a grouping by an
/// expression, the functions DATEADD, DATEDIFF, ROUND, SQRT and CAST, the ranking functions
/// over a window, and the aggregate into a list. Each is read where its language has it,
/// written where its target's language has it - by a spelling verified against the pinned
/// release - and written in native SQL with a record of kind Fallback everywhere else; what
/// no target writes, the rule of grouping and the place of a window among them, is refused
/// once, by the builder template. The categories of T2 carry the shapes through every
/// direction and the fourth level (<c>QueryShapeMatrixTest</c>, the differential matrix);
/// this class names the rules and the spellings.
/// </summary>
public class ExpressionVocabularyTest
{
    private static EntityMap Sales()
    {
        var id = new Property { Name = "SaleId", Type = LangType.Scalar(ScalarType.Int) };
        var customer = new Property { Name = "CustomerId", Type = LangType.Scalar(ScalarType.Int) };
        var soldAt = new Property { Name = "SoldAt", Type = LangType.Scalar(ScalarType.DateTime) };
        var total = new Property { Name = "Total", Type = LangType.Scalar(ScalarType.Decimal) };
        var quantity = new Property { Name = "Quantity", Type = LangType.Scalar(ScalarType.Int) };
        var code = new Property { Name = "Code", Type = LangType.Scalar(ScalarType.String) };
        var notes = new Property { Name = "Notes", Type = LangType.Scalar(ScalarType.String, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Sale", Properties = [id, customer, soldAt, total, quantity, code, notes] },
            Table = "Sales",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "SaleId" },
                new PropertyMap { Property = customer, ColumnName = "CustomerId" },
                new PropertyMap { Property = soldAt, ColumnName = "SoldAt" },
                new PropertyMap { Property = total, ColumnName = "Total" },
                new PropertyMap { Property = quantity, ColumnName = "Quantity" },
                new PropertyMap { Property = code, ColumnName = "Code" },
                new PropertyMap { Property = notes, ColumnName = "Notes" },
            ],
        };
    }

    private static AbstractQueryBuilder Builder(ORMEnum target) => target switch
    {
        ORMEnum.Dapper => new DapperSqlQueryBuilder(),
        ORMEnum.MyBatis => new MyBatisSqlQueryBuilder(),
        ORMEnum.EFCore => new EFCoreLinqQueryBuilder(),
        ORMEnum.NHibernate => new NHibernateHqlQueryBuilder(),
        ORMEnum.Hibernate => new HibernateJpqlQueryBuilder(),
        ORMEnum.EclipseLink => new EclipseLinkJpqlQueryBuilder(),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static AbstractQueryBuilder Sql(ORMEnum target, string sql)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Sales()];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [Sales()]);
        return builder;
    }

    private static AbstractQueryBuilder Linq(ORMEnum target, string chain)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Sales()];
        var source = $$"""
            public void Query()
            {
                var q = ctx.Sales.{{chain}}.ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, [Sales()]);
        return builder;
    }

    private static AbstractQueryBuilder Hql(ORMEnum target, string hql)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Sales()];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, hql, [Sales()]);
        return builder;
    }

    private static AbstractQueryBuilder Jpql(ORMEnum target, string jpql, bool hibernate = true)
    {
        var builder = Builder(target);
        builder.EntityMaps = [Sales()];
        JakartaPersistence.JpqlQueryParser parser = hibernate
            ? new HibernateJpqlQueryParser(() => builder)
            : new EclipseLinkJpqlQueryParser(() => builder);
        parser.Parse(ConversionContentType.JpqlQuery, jpql, [Sales()]);
        return builder;
    }

    private static string Text(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Fallback);

        var query = built.FirstOrDefault(s => s.ContentType is ConversionContentType.SqlQuery or ConversionContentType.HqlQuery or ConversionContentType.JpqlQuery)
            ?? built.First();
        return OneLine(query.Content);
    }

    /// <summary>The query a target wrote in native SQL with a record of kind Fallback naming the feature (decision 113); the text is the bare SQL beside the method.</summary>
    private static string FellBack(AbstractQueryBuilder builder, QueryFeature feature)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == feature);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return OneLine(built.Single(s => s.ContentType == ConversionContentType.SqlQuery).Content);
    }

    private static ConversionRecord Refused(AbstractQueryBuilder builder, QueryFeature feature)
    {
        Assert.Empty(builder.Build());
        var record = builder.Records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature);
        Assert.True(record is not null, "No Failure under " + feature + ":\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return record!;
    }

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    // ---- grouping by an expression --------------------------------------------------------

    private const string ByYear = "SELECT YEAR(s.SoldAt) AS SoldYear, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)";

    private const string ByYearAndCustomer =
        "SELECT YEAR(s.SoldAt) AS SoldYear, s.CustomerId AS CustomerId, SUM(s.Total) AS Total FROM Sales AS s GROUP BY YEAR(s.SoldAt), s.CustomerId";

    /// <summary>T-SQL reads the key as the expression it is and writes it back in the projection and the grouping alike.</summary>
    [Fact]
    public void AGroupingByAnExpressionRoundTripsInTSql()
    {
        Assert.Equal("SELECT YEAR(s.SoldAt) AS SoldYear, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)", Text(Sql(ORMEnum.Dapper, ByYear)));
    }

    /// <summary>
    /// Every target writes the grouping in its own language: HQL 5.7 and 7.4 and EclipseLink's
    /// JPQL group by the expression as written (verified), LINQ groups by the expression and
    /// reads the projected value off g.Key.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.NHibernate, "select year(s.SoldAt) as SoldYear, count(*) as Sold from Sale s group by year(s.SoldAt)")]
    [InlineData(ORMEnum.Hibernate, "select extract(year from s.SoldAt) as SoldYear, count(s) as Sold from Sale s group by extract(year from s.SoldAt)")]
    [InlineData(ORMEnum.EclipseLink, "select extract(year from s.SoldAt) as SoldYear, count(s) as Sold from Sale s group by extract(year from s.SoldAt)")]
    [InlineData(ORMEnum.EFCore, ".GroupBy(s => s.SoldAt.Year) .Select(g => new { SoldYear = g.Key, Sold = g.Count() })")]
    public void EveryTargetGroupsByTheExpression(ORMEnum target, string expected)
    {
        Assert.Contains(expected, Text(Sql(target, ByYear)), StringComparison.Ordinal);
    }

    /// <summary>A LINQ key of several parts names each: a column by its property, an expression by the alias of the projection that projects it.</summary>
    [Fact]
    public void ALinqKeyOfSeveralPartsNamesTheExpressionByItsProjection()
    {
        var linq = Text(Sql(ORMEnum.EFCore, ByYearAndCustomer));

        Assert.Contains(".GroupBy(s => new { SoldYear = s.SoldAt.Year, s.CustomerId })", linq, StringComparison.Ordinal);
        Assert.Contains("SoldYear = g.Key.SoldYear, CustomerId = g.Key.CustomerId, Total = g.Sum(s => s.Total)", linq, StringComparison.Ordinal);
    }

    /// <summary>An expression of a key of several parts that no projection names has no name LINQ could give it (decision 028), so EF Core writes the query in native SQL.</summary>
    [Fact]
    public void AnUnnamedExpressionInAKeyOfSeveralPartsFallsBackInEFCore()
    {
        var sql = FellBack(
            Sql(ORMEnum.EFCore, "SELECT s.CustomerId AS CustomerId, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt), s.CustomerId"),
            QueryFeature.ComputedGrouping);

        Assert.Contains("GROUP BY YEAR(s.SoldAt), s.CustomerId", sql, StringComparison.Ordinal);
    }

    private const string PairsOfSales =
        "SELECT s.SaleId AS FirstSale, o.SaleId AS SecondSale, COUNT(*) AS Pairs FROM Sales AS s INNER JOIN Sales AS o ON o.CustomerId = s.CustomerId GROUP BY s.SaleId, o.SaleId";

    /// <summary>
    /// Two key columns whose properties share a name - the key of two tables, here one table
    /// twice - would infer one member of the key object twice, which C# refuses (CS0833); each
    /// goes under the alias of the projection that projects it, and the projection reads it there.
    /// </summary>
    [Fact]
    public void TwoKeyColumnsOfOneNameGoUnderTheirProjections()
    {
        var linq = Text(Sql(ORMEnum.EFCore, PairsOfSales));

        Assert.Contains(".GroupBy(t => new { FirstSale = t.s.SaleId, SecondSale = t.o.SaleId })", linq, StringComparison.Ordinal);
        Assert.Contains("FirstSale = g.Key.FirstSale, SecondSale = g.Key.SecondSale, Pairs = g.Count()", linq, StringComparison.Ordinal);
    }

    /// <summary>Two such columns that no projection names have no names LINQ could give them (decision 028), so EF Core writes the query in native SQL.</summary>
    [Fact]
    public void TwoUnnamedKeyColumnsOfOneNameFallBackInEFCore()
    {
        var sql = FellBack(
            Sql(ORMEnum.EFCore, "SELECT COUNT(*) AS Pairs FROM Sales AS s INNER JOIN Sales AS o ON o.CustomerId = s.CustomerId GROUP BY s.SaleId, o.SaleId"),
            QueryFeature.Grouping);

        Assert.Contains("GROUP BY s.SaleId, o.SaleId", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// COALESCE over a value C# types as non-nullable - SUM over an int, a member on the optional
    /// side of an outer join - is lifted to the nullable type C#'s ?? takes on its left (CS0019
    /// otherwise): an aggregate through its element, which EF Core translates to the bare SUM;
    /// a value that may be null already - a nullable text - stays as it is.
    /// </summary>
    [Fact]
    public void CoalesceLiftsANonNullableLeftSideInLinq()
    {
        var grouped = Text(Sql(
            ORMEnum.EFCore,
            "SELECT s.CustomerId AS CustomerId, COALESCE(SUM(o.Quantity), 0) AS Later, COALESCE(MAX(o.Notes), '') AS Note FROM Sales AS s LEFT JOIN Sales AS o ON o.CustomerId = s.CustomerId GROUP BY s.CustomerId"));

        Assert.Contains("Later = (g.Sum(t => (int?)t.o.Quantity) ?? 0)", grouped, StringComparison.Ordinal);
        Assert.Contains("Note = (g.Max(t => t.o.Notes) ?? \"\")", grouped, StringComparison.Ordinal);

        var rows = Text(Sql(ORMEnum.EFCore, "SELECT s.SaleId AS SaleId, COALESCE(o.Quantity, 0) AS Quantity FROM Sales AS s LEFT JOIN Sales AS o ON o.SaleId = s.SaleId"));
        Assert.Contains("Quantity = ((int?)t.o.Quantity ?? 0)", rows, StringComparison.Ordinal);
    }

    /// <summary>
    /// EclipseLink binds every literal as a parameter (measured): a key with a literal in it
    /// would reach SQL Server as another expression than the projection, so EclipseLink writes
    /// it in native SQL - and a key without one in JPQL.
    /// </summary>
    [Fact]
    public void EclipseLinkWritesAKeyWithALiteralInNativeSql()
    {
        const string byBand = "SELECT CASE WHEN s.Quantity > 2 THEN 1 ELSE 0 END AS Band, COUNT(*) AS Sold FROM Sales AS s GROUP BY CASE WHEN s.Quantity > 2 THEN 1 ELSE 0 END";

        var sql = FellBack(Sql(ORMEnum.EclipseLink, byBand), QueryFeature.ComputedGrouping);
        Assert.Contains("GROUP BY CASE WHEN s.Quantity > 2 THEN 1 ELSE 0 END", sql, StringComparison.Ordinal);

        Assert.Contains("group by case when s.Quantity > 2 then 1 else 0 end", Text(Sql(ORMEnum.Hibernate, byBand)), StringComparison.Ordinal);
        Assert.Contains("group by case when s.Quantity > 2 then 1 else 0 end", Text(Sql(ORMEnum.NHibernate, byBand)), StringComparison.Ordinal);
    }

    /// <summary>What LINQ, HQL and JPQL write reads back as the same grouping: g.Key is the key's value, in a projection and inside an expression over it.</summary>
    [Fact]
    public void EveryReaderReadsTheGroupingBack()
    {
        Assert.Equal(
            "SELECT YEAR(s.SoldAt) AS SoldYear, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)",
            Text(Linq(ORMEnum.Dapper, "GroupBy(s => s.SoldAt.Year).Select(g => new { SoldYear = g.Key, Sold = g.Count() })")));
        Assert.Equal(
            "SELECT YEAR(s.SoldAt) AS SoldYear, s.CustomerId AS CustomerId, SUM(s.Total) AS Total FROM Sales AS s GROUP BY YEAR(s.SoldAt), s.CustomerId",
            Text(Linq(ORMEnum.Dapper, "GroupBy(s => new { SoldYear = s.SoldAt.Year, s.CustomerId }).Select(g => new { SoldYear = g.Key.SoldYear, CustomerId = g.Key.CustomerId, Total = g.Sum(x => x.Total) })")));
        Assert.Equal(
            "SELECT YEAR(s.SoldAt) + 1 AS NextYear FROM Sales AS s GROUP BY YEAR(s.SoldAt)",
            Text(Linq(ORMEnum.Dapper, "GroupBy(s => s.SoldAt.Year).Select(g => new { NextYear = g.Key + 1 })")));
        Assert.Equal(
            "SELECT YEAR(s.SoldAt) AS SoldYear, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)",
            Text(Hql(ORMEnum.Dapper, "select year(s.SoldAt) as SoldYear, count(*) as Sold from Sale s group by year(s.SoldAt)")));
        Assert.Equal(
            "SELECT YEAR(s.SoldAt) AS SoldYear, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)",
            Text(Jpql(ORMEnum.Dapper, "select extract(year from s.SoldAt) as SoldYear, count(*) as Sold from Sale s group by extract(year from s.SoldAt)", hibernate: false)));
    }

    /// <summary>
    /// The rule of grouping, held for every target: beside its keys a grouped query names a
    /// column only under an aggregate or inside a value equal to a key - a value computed over
    /// the key passes, another part of the same column does not.
    /// </summary>
    [Fact]
    public void AColumnOutsideEveryKeyAndAggregateRefuses()
    {
        var record = Refused(Sql(ORMEnum.Dapper, "SELECT s.CustomerId AS CustomerId, COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt)"), QueryFeature.Grouping);
        Assert.Contains("s.CustomerId", record.Reason, StringComparison.Ordinal);

        Refused(Sql(ORMEnum.EFCore, "SELECT MONTH(s.SoldAt) AS SoldMonth FROM Sales AS s GROUP BY YEAR(s.SoldAt)"), QueryFeature.Grouping);
        Refused(Sql(ORMEnum.Dapper, "SELECT COUNT(*) AS Sold FROM Sales AS s GROUP BY YEAR(s.SoldAt) HAVING MAX(s.Total) > s.Quantity"), QueryFeature.Grouping);
        Refused(Sql(ORMEnum.Dapper, "SELECT YEAR(s.SoldAt) AS SoldYear FROM Sales AS s GROUP BY YEAR(s.SoldAt) ORDER BY s.SoldAt"), QueryFeature.Grouping);

        Assert.Contains("YEAR(s.SoldAt) + 1 AS NextYear", Text(Sql(ORMEnum.Dapper, "SELECT YEAR(s.SoldAt) + 1 AS NextYear FROM Sales AS s GROUP BY YEAR(s.SoldAt)")), StringComparison.Ordinal);
    }

    // ---- functions ------------------------------------------------------------------------

    /// <summary>DATEADD and DATEDIFF with their unit, the abbreviations of the datepart read as the unit they abbreviate.</summary>
    [Fact]
    public void TheDateFunctionsRoundTripInTSql()
    {
        Assert.Equal(
            "SELECT DATEADD(day, 30, s.SoldAt) AS DueAt, DATEDIFF(minute, s.SoldAt, CURRENT_TIMESTAMP) AS Age FROM Sales AS s",
            Text(Sql(ORMEnum.Dapper, "SELECT DATEADD(dd, 30, s.SoldAt) AS DueAt, DATEDIFF(mi, s.SoldAt, CURRENT_TIMESTAMP) AS Age FROM Sales AS s")));
    }

    /// <summary>
    /// Each target writes the date functions its language has: LINQ the Add methods and
    /// EF.Functions.DateDiff…, HQL 7.4 timestampadd and timestampdiff - which it writes as
    /// DATEADD and DATEDIFF_BIG, counting boundaries (verified). HQL 5.7 and JPQL have none,
    /// and NHibernate and EclipseLink write the query in native SQL.
    /// </summary>
    [Fact]
    public void EachTargetWritesTheDateFunctionsItHas()
    {
        const string sql = "SELECT DATEADD(hour, s.Quantity, s.SoldAt) AS DueAt, DATEDIFF(day, s.SoldAt, CURRENT_TIMESTAMP) AS Age FROM Sales AS s";

        var linq = Text(Sql(ORMEnum.EFCore, sql));
        Assert.Contains("DueAt = s.SoldAt.AddHours(s.Quantity)", linq, StringComparison.Ordinal);
        Assert.Contains("Age = EF.Functions.DateDiffDay(s.SoldAt, DateTime.Now)", linq, StringComparison.Ordinal);

        Assert.Equal(
            "select timestampadd(hour, s.Quantity, s.SoldAt) as DueAt, timestampdiff(day, s.SoldAt, current_timestamp) as Age from Sale s",
            Text(Sql(ORMEnum.Hibernate, sql)));

        Assert.Equal(OneLine(sql), FellBack(Sql(ORMEnum.NHibernate, sql), QueryFeature.Expression));
        Assert.Equal(OneLine(sql), FellBack(Sql(ORMEnum.EclipseLink, sql), QueryFeature.Expression));
    }

    /// <summary>
    /// EF.Functions.DateDiff… takes two values of one type, so a date against a moment has no
    /// overload C# can choose and the call does not compile (measured against EF Core
    /// 10.0.10): EF Core writes such a query in native SQL, and two values of one type stay
    /// in LINQ.
    /// </summary>
    [Fact]
    public void EFCoreWritesADateDifferenceOverTwoTypesInNativeSql()
    {
        static EntityMap Loans()
        {
            var id = new Property { Name = "LoanId", Type = LangType.Scalar(ScalarType.Int) };
            var due = new Property { Name = "DueOn", Type = LangType.Scalar(ScalarType.Date) };
            var loaned = new Property { Name = "LoanedAt", Type = LangType.Scalar(ScalarType.DateTime) };
            var returned = new Property { Name = "ReturnedAt", Type = LangType.Scalar(ScalarType.DateTime, isNullable: true) };

            return new EntityMap
            {
                Entity = new Entity { Name = "Loan", Properties = [id, due, loaned, returned] },
                Table = "Loans",
                PropertyMaps = [.. new[] { id, due, loaned, returned }.Select(p => new PropertyMap { Property = p, ColumnName = p.Name })],
            };
        }

        static AbstractQueryBuilder Read(string sql)
        {
            var builder = new EFCoreLinqQueryBuilder { EntityMaps = [Loans()] };
            new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [Loans()]);
            return builder;
        }

        const string mixed = "SELECT l.LoanId AS LoanId, DATEDIFF(day, l.DueOn, l.ReturnedAt) AS DaysLate FROM Loans AS l";
        Assert.Equal(mixed, FellBack(Read(mixed), QueryFeature.Expression));

        Assert.Contains(
            "DaysOut = EF.Functions.DateDiffDay(l.LoanedAt, l.ReturnedAt)",
            Text(Read("SELECT l.LoanId AS LoanId, DATEDIFF(day, l.LoanedAt, l.ReturnedAt) AS DaysOut FROM Loans AS l")),
            StringComparison.Ordinal);
    }

    /// <summary>What LINQ and HQL 7.4 write reads back as the same call.</summary>
    [Fact]
    public void TheDateFunctionsReadBack()
    {
        const string expected = "SELECT DATEADD(hour, s.Quantity, s.SoldAt) AS DueAt, DATEDIFF(day, s.SoldAt, CURRENT_TIMESTAMP) AS Age FROM Sales AS s";

        Assert.Equal(expected, Text(Linq(ORMEnum.Dapper, "Select(s => new { DueAt = s.SoldAt.AddHours(s.Quantity), Age = EF.Functions.DateDiffDay(s.SoldAt, DateTime.Now) })")));
        Assert.Equal(expected, Text(Jpql(ORMEnum.Dapper, "select timestampadd(hour, s.Quantity, s.SoldAt) as DueAt, timestampdiff(day, s.SoldAt, current_timestamp) as Age from Sale s")));
    }

    /// <summary>
    /// A moment T-SQL writes as a string inside a date function takes the scalar of its
    /// position, as a string compared with a moment column does, so that LINQ and JPQL get a
    /// moment; a string that reads as no moment refuses.
    /// </summary>
    [Fact]
    public void AStringMomentInADateFunctionIsTypedByItsPosition()
    {
        const string sql = "SELECT DATEDIFF(hour, '2025-01-01', s.SoldAt) AS Hours FROM Sales AS s";

        Assert.Contains("EF.Functions.DateDiffHour(DateTime.Parse(\"2025-01-01 00:00:00\"), s.SoldAt)", Text(Sql(ORMEnum.EFCore, sql)), StringComparison.Ordinal);
        Assert.Contains("timestampdiff(hour, {ts '2025-01-01 00:00:00'}, s.SoldAt)", Text(Sql(ORMEnum.Hibernate, sql)), StringComparison.Ordinal);
        Assert.Contains("DATEDIFF(hour, '2025-01-01 00:00:00', s.SoldAt)", Text(Sql(ORMEnum.Dapper, sql)), StringComparison.Ordinal);

        Refused(Sql(ORMEnum.Dapper, "SELECT DATEDIFF(hour, 'soon', s.SoldAt) AS Hours FROM Sales AS s"), QueryFeature.Expression);
    }

    /// <summary>A parameter inside a date function takes its scalar from its position: the count is a whole number, a moment a moment.</summary>
    [Fact]
    public void AParameterOfADateFunctionIsTypedByItsPosition()
    {
        var method = Sql(ORMEnum.Dapper, "SELECT s.SaleId AS SaleId FROM Sales AS s WHERE DATEADD(day, @days, s.SoldAt) > @asOf AND DATEDIFF(day, @since, s.SoldAt) > 0")
            .Build().Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains("int days", method, StringComparison.Ordinal);
        Assert.Contains("DateTime asOf", method, StringComparison.Ordinal);
        Assert.Contains("DateTime since", method, StringComparison.Ordinal);
    }

    /// <summary>ROUND and SQRT in every language that writes them, read back from each; LINQ casts a decimal to double for Math.Sqrt, as SQL Server converts it anyway.</summary>
    [Fact]
    public void RoundAndSqrtGoEverywhere()
    {
        const string sql = "SELECT ROUND(s.Total, 1) AS Rounded, SQRT(s.Total) AS Root FROM Sales AS s";

        Assert.Equal(OneLine(sql), Text(Sql(ORMEnum.Dapper, sql)));
        Assert.Contains("Rounded = Math.Round(s.Total, 1), Root = Math.Sqrt((double)(s.Total))", Text(Sql(ORMEnum.EFCore, sql)), StringComparison.Ordinal);
        Assert.Equal("select round(s.Total, 1) as Rounded, sqrt(s.Total) as Root from Sale s", Text(Sql(ORMEnum.NHibernate, sql)));
        Assert.Equal("select round(s.Total, 1) as Rounded, sqrt(s.Total) as Root from Sale s", Text(Sql(ORMEnum.Hibernate, sql)));
        Assert.Equal("select round(s.Total, 1) as Rounded, sqrt(s.Total) as Root from Sale s", Text(Sql(ORMEnum.EclipseLink, sql)));

        // The cast LINQ needs for Math.Sqrt is the overload's, not the query's: SQRT converts
        // its argument itself, so it reads back as the argument.
        Assert.Equal(OneLine(sql), Text(Linq(ORMEnum.Dapper, "Select(s => new { Rounded = Math.Round(s.Total, 1), Root = Math.Sqrt((double)(s.Total)) })")));
        Assert.Equal(OneLine(sql), Text(Hql(ORMEnum.Dapper, "select round(s.Total, 1) as Rounded, sqrt(s.Total) as Root from Sale s")));
        Assert.Equal(OneLine(sql), Text(Jpql(ORMEnum.Dapper, "select round(s.Total, 1) as Rounded, sqrt(s.Total) as Root from Sale s", hibernate: false)));
    }

    /// <summary>Math.Round without places is ROUND to whole units, the one form T-SQL has; a ROUND that truncates is refused by name.</summary>
    [Fact]
    public void RoundTakesTwoArgumentsAlways()
    {
        Assert.Contains("ROUND(s.Total, 0) AS Rounded", Text(Linq(ORMEnum.Dapper, "Select(s => new { Rounded = Math.Round(s.Total) })")), StringComparison.Ordinal);

        var truncating = Sql(ORMEnum.Dapper, "SELECT s.SaleId AS SaleId, ROUND(s.Total, 1, 1) AS Cut FROM Sales AS s");
        Assert.NotEmpty(truncating.Build());
        Assert.Contains(truncating.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("truncates", StringComparison.Ordinal));
    }

    /// <summary>Math.Round over a whole number has no overload C# can choose, so EF Core writes the query in native SQL.</summary>
    [Fact]
    public void RoundOverAWholeNumberFallsBackInEFCore()
    {
        FellBack(Sql(ORMEnum.EFCore, "SELECT ROUND(s.Quantity, -1) AS Tens FROM Sales AS s"), QueryFeature.Expression);
    }

    /// <summary>
    /// CAST into the five scalars of Jakarta Persistence 3.2, written in T-SQL without a length
    /// or a precision that could change the value; a conversion into text concatenates.
    /// </summary>
    [Fact]
    public void ACastRoundTripsInTSqlAndConcatenates()
    {
        const string sql = "SELECT '#' + CAST(s.SaleId AS NVARCHAR(MAX)) AS Label, CAST(s.Quantity AS BIGINT) AS Big, CAST(s.Total AS FLOAT) AS Approximate, CAST(s.Quantity AS REAL) AS Single, CAST(s.Total AS INT) AS Whole FROM Sales AS s";

        Assert.Equal(sql, Text(Sql(ORMEnum.Dapper, sql)));
        Assert.Contains(
            "Label = \"#\" + s.SaleId.ToString(), Big = (long)(s.Quantity), Approximate = (double)(s.Total), Single = (float)(s.Quantity), Whole = (int)(s.Total)",
            Text(Sql(ORMEnum.EFCore, sql)),
            StringComparison.Ordinal);
        Assert.Contains(
            "select concat('#', cast(s.SaleId as string)) as Label, cast(s.Quantity as long) as Big, cast(s.Total as double) as Approximate, cast(s.Quantity as float) as Single, cast(s.Total as int) as Whole",
            Text(Sql(ORMEnum.NHibernate, sql)),
            StringComparison.Ordinal);
        Assert.Contains(
            "select concat('#', cast(s.SaleId as String)) as Label, cast(s.Quantity as Long) as Big, cast(s.Total as Double) as Approximate, cast(s.Quantity as Float) as Single, cast(s.Total as Integer) as Whole",
            Text(Sql(ORMEnum.Hibernate, sql)),
            StringComparison.Ordinal);
    }

    /// <summary>What LINQ, HQL and JPQL write reads back as the same conversion.</summary>
    [Fact]
    public void ACastReadsBack()
    {
        const string expected = "SELECT '#' + CAST(s.SaleId AS NVARCHAR(MAX)) AS Label, CAST(s.Quantity AS BIGINT) AS Big FROM Sales AS s";

        Assert.Equal(expected, Text(Linq(ORMEnum.Dapper, "Select(s => new { Label = \"#\" + s.SaleId.ToString(), Big = (long)(s.Quantity) })")));
        Assert.Equal(expected, Text(Hql(ORMEnum.Dapper, "select concat('#', cast(s.SaleId as string)) as Label, cast(s.Quantity as long) as Big from Sale s")));
        Assert.Equal(expected, Text(Jpql(ORMEnum.Dapper, "select concat('#', cast(s.SaleId as String)) as Label, cast(s.Quantity as Long) as Big from Sale s")));
    }

    /// <summary>A conversion with a length, into text that is not unicode, or into a decimal could cut or change the value, which the model does not carry: refused by name.</summary>
    [Theory]
    [InlineData("CAST(s.SaleId AS NVARCHAR(10))")]
    [InlineData("CAST(s.SaleId AS VARCHAR(MAX))")]
    [InlineData("CAST(s.Total AS DECIMAL(10, 2))")]
    public void ACastThatCouldChangeTheValueRefuses(string cast)
    {
        var record = Refused(Sql(ORMEnum.Dapper, $"SELECT s.SaleId AS SaleId FROM Sales AS s WHERE {cast} IS NOT NULL"), QueryFeature.Expression);
        Assert.Contains("conversion", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A conversion of text into text: HQL 5.7 converts into NVARCHAR(4000) and HQL 7.4 into
    /// varchar(max) (both verified), narrower than the NVARCHAR(MAX) of the model, so over a
    /// text both write the query in native SQL; EclipseLink has no cast that SQL Server takes.
    /// </summary>
    [Fact]
    public void ACastOfTextFallsBackWhereTheTargetConvertsNarrower()
    {
        const string sql = "SELECT CAST(s.Code AS NVARCHAR(MAX)) + '!' AS Label FROM Sales AS s";

        FellBack(Sql(ORMEnum.NHibernate, sql), QueryFeature.Expression);
        FellBack(Sql(ORMEnum.Hibernate, sql), QueryFeature.Expression);
        FellBack(Sql(ORMEnum.EclipseLink, "SELECT CAST(s.SaleId AS NVARCHAR(MAX)) AS Label FROM Sales AS s"), QueryFeature.Expression);
    }

    // ---- ranking functions over a window ----------------------------------------------------

    private const string Ranked =
        "SELECT s.SaleId AS SaleId, ROW_NUMBER() OVER (PARTITION BY s.CustomerId ORDER BY s.SoldAt DESC) AS RowNumber, RANK() OVER (ORDER BY s.Total DESC) AS TotalRank, DENSE_RANK() OVER (PARTITION BY s.CustomerId, s.Code ORDER BY s.Quantity ASC, s.SaleId ASC) AS QuantityRank FROM Sales AS s";

    /// <summary>T-SQL reads and writes the three ranking functions with their partitions and ordering.</summary>
    [Fact]
    public void TheRankingFunctionsRoundTripInTSql()
    {
        Assert.Equal(Ranked, Text(Sql(ORMEnum.Dapper, Ranked)));
    }

    /// <summary>HQL 7.4 writes them as SQL does (verified), and what it writes reads back.</summary>
    [Fact]
    public void HibernateWritesTheRankingFunctions()
    {
        const string hql = "select s.SaleId as SaleId, row_number() over (partition by s.CustomerId order by s.SoldAt desc) as RowNumber, rank() over (order by s.Total desc) as TotalRank, dense_rank() over (partition by s.CustomerId, s.Code order by s.Quantity asc, s.SaleId asc) as QuantityRank from Sale s";

        Assert.Equal(hql, Text(Sql(ORMEnum.Hibernate, Ranked)));
        Assert.Equal(Ranked, Text(Jpql(ORMEnum.Dapper, hql)));
    }

    /// <summary>LINQ, HQL 5.7 and JPQL have no window, and the three targets write the query in native SQL.</summary>
    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void ATargetWithoutAWindowFallsBack(ORMEnum target)
    {
        Assert.Equal(Ranked, FellBack(Sql(target, Ranked), QueryFeature.WindowFunction));
    }

    /// <summary>
    /// The best row of every group: the window in an intermediate result and the filter over
    /// it, as anyone writes it - in T-SQL and HQL 7.4, and in native SQL elsewhere.
    /// </summary>
    [Fact]
    public void TheBestRowOfAGroupGoesThroughAnIntermediateResult()
    {
        const string sql = """
            WITH Latest AS (
                SELECT s.SaleId AS SaleId, s.CustomerId AS CustomerId, ROW_NUMBER() OVER (PARTITION BY s.CustomerId ORDER BY s.SoldAt DESC) AS RowNumber
                FROM Sales AS s)
            SELECT l.SaleId AS SaleId, l.CustomerId AS CustomerId FROM Latest AS l WHERE l.RowNumber = 1
            """;

        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY s.CustomerId ORDER BY s.SoldAt DESC) AS RowNumber", Text(Sql(ORMEnum.Dapper, sql)), StringComparison.Ordinal);
        Assert.Contains("row_number() over (partition by s.CustomerId order by s.SoldAt desc) as RowNumber", Text(Sql(ORMEnum.Hibernate, sql)), StringComparison.Ordinal);
        Assert.Contains("WHERE l.RowNumber = 1", FellBack(Sql(ORMEnum.EFCore, sql), QueryFeature.WindowFunction), StringComparison.Ordinal);
    }

    /// <summary>A window stands in a projection and nowhere else - not in a filter, not under an aggregate (decision 113); SQL Server computes it nowhere else.</summary>
    [Fact]
    public void AWindowOutsideAProjectionRefuses()
    {
        Refused(Sql(ORMEnum.Dapper, "SELECT s.SaleId AS SaleId FROM Sales AS s ORDER BY ROW_NUMBER() OVER (ORDER BY s.SoldAt)"), QueryFeature.WindowFunction);
        Refused(Sql(ORMEnum.Dapper, "SELECT MAX(ROW_NUMBER() OVER (ORDER BY s.SoldAt)) AS Last FROM Sales AS s"), QueryFeature.WindowFunction);
    }

    /// <summary>An aggregate over a window and a frame of rows compute something the vocabulary does not carry: refused by name.</summary>
    [Fact]
    public void AnAggregateOverAWindowIsNotRead()
    {
        var read = Sql(ORMEnum.Dapper, "SELECT s.SaleId AS SaleId, SUM(s.Total) OVER (PARTITION BY s.CustomerId) AS Running FROM Sales AS s");
        Assert.NotEmpty(read.Build());
        Assert.Contains(read.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.WindowFunction);
    }

    // ---- the aggregate into a list ----------------------------------------------------------

    private const string Listed =
        "SELECT s.CustomerId AS CustomerId, STRING_AGG(s.Code, ', ') WITHIN GROUP (ORDER BY s.Code ASC) AS Codes FROM Sales AS s GROUP BY s.CustomerId";

    /// <summary>T-SQL reads and writes STRING_AGG with its separator and its ordering.</summary>
    [Fact]
    public void AListRoundTripsInTSql()
    {
        Assert.Equal(Listed, Text(Sql(ORMEnum.Dapper, Listed)));
        Assert.Equal(
            "SELECT s.CustomerId AS CustomerId, STRING_AGG(s.Code, ';') AS Codes FROM Sales AS s GROUP BY s.CustomerId",
            Text(Sql(ORMEnum.Dapper, "SELECT s.CustomerId AS CustomerId, STRING_AGG(s.Code, ';') AS Codes FROM Sales AS s GROUP BY s.CustomerId")));
    }

    /// <summary>HQL 7.4 writes listagg, which it writes as STRING_AGG (verified); LINQ writes string.Join over the elements of the group, ordered by the steps before its Select.</summary>
    [Fact]
    public void HibernateAndEFCoreWriteTheList()
    {
        Assert.Equal(
            "select s.CustomerId as CustomerId, listagg(s.Code, ', ') within group (order by s.Code asc) as Codes from Sale s group by s.CustomerId",
            Text(Sql(ORMEnum.Hibernate, Listed)));
        Assert.Contains(
            "Codes = string.Join(\", \", g.OrderBy(s => s.Code).Select(s => s.Code))",
            Text(Sql(ORMEnum.EFCore, Listed)),
            StringComparison.Ordinal);
    }

    /// <summary>What both write reads back as the same aggregate.</summary>
    [Fact]
    public void TheListReadsBack()
    {
        Assert.Equal(Listed, Text(Jpql(ORMEnum.Dapper, "select s.CustomerId as CustomerId, listagg(s.Code, ', ') within group (order by s.Code asc) as Codes from Sale s group by s.CustomerId")));
        Assert.Equal(Listed, Text(Linq(ORMEnum.Dapper, "GroupBy(s => s.CustomerId).Select(g => new { CustomerId = g.Key, Codes = string.Join(\", \", g.OrderBy(x => x.Code).Select(x => x.Code)) })")));
    }

    /// <summary>
    /// EF Core 10 turns every NULL into the empty string before STRING_AGG and joins a list
    /// over a subquery on the client (both verified), so over a column that may hold NULL and
    /// over a subquery EF Core writes the query in native SQL; read from LINQ over such a
    /// column, the difference is said in a record. HQL 5.7 and JPQL have no list at all.
    /// </summary>
    [Fact]
    public void AListFallsBackWhereTheTargetJoinsOtherwise()
    {
        const string nullable = "SELECT s.CustomerId AS CustomerId, STRING_AGG(s.Notes, ', ') AS Notes FROM Sales AS s GROUP BY s.CustomerId";
        const string correlated = "SELECT s.SaleId AS SaleId, (SELECT STRING_AGG(o.Code, ', ') FROM Sales AS o WHERE o.CustomerId = s.CustomerId) AS Codes FROM Sales AS s";

        Assert.Equal(nullable, FellBack(Sql(ORMEnum.EFCore, nullable), QueryFeature.ListAggregation));
        Assert.Contains("(SELECT STRING_AGG(o.Code, ', ') FROM Sales AS o WHERE o.CustomerId = s.CustomerId) AS Codes", FellBack(Sql(ORMEnum.EFCore, correlated), QueryFeature.ListAggregation), StringComparison.Ordinal);
        Assert.Contains("(select listagg(o.Code, ', ') from Sale o where o.CustomerId = s.CustomerId) as Codes", Text(Sql(ORMEnum.Hibernate, correlated)), StringComparison.Ordinal);
        Assert.Equal(Listed, FellBack(Sql(ORMEnum.NHibernate, Listed), QueryFeature.ListAggregation));
        Assert.Equal(Listed, FellBack(Sql(ORMEnum.EclipseLink, Listed), QueryFeature.ListAggregation));

        var read = Linq(ORMEnum.Dapper, "GroupBy(s => s.CustomerId).Select(g => new { CustomerId = g.Key, Notes = string.Join(\", \", g.Select(x => x.Notes)) })");
        Assert.Equal(nullable, Text(read));
        Assert.Contains(read.Records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.ListAggregation);
    }

    /// <summary>A list is an aggregate: nothing aggregates over it, it aggregates over nothing that aggregates, and a recursive member does not hold it.</summary>
    [Fact]
    public void AListIsAnAggregate()
    {
        Refused(Sql(ORMEnum.Dapper, "SELECT COUNT(STRING_AGG(s.Code, ',')) AS N FROM Sales AS s"), QueryFeature.Expression);
        Refused(Sql(ORMEnum.Dapper, "SELECT STRING_AGG(s.Code, ',') WITHIN GROUP (ORDER BY COUNT(*) ASC) AS Codes FROM Sales AS s GROUP BY s.CustomerId"), QueryFeature.ListAggregation);
        Refused(
            Sql(ORMEnum.Dapper, """
                WITH Walk AS (
                    SELECT s.SaleId AS SaleId, s.Code AS Codes FROM Sales AS s WHERE s.SaleId = 1
                    UNION ALL
                    SELECT n.SaleId, STRING_AGG(n.Code, ',') FROM Walk AS w INNER JOIN Sales AS n ON n.SaleId = w.SaleId + 1)
                SELECT w.Codes AS Codes FROM Walk AS w
                """),
            QueryFeature.IntermediateResult);
    }

    // ---- the descriptors ------------------------------------------------------------------

    // ---- ordering of a grouped query before the projection ---------------------------------

    private const string TopByCountThenKey =
        "SELECT TOP (3) s.CustomerId AS CustomerId, COUNT(*) AS Sold FROM Sales AS s GROUP BY s.CustomerId, s.Code ORDER BY Sold DESC, s.Code ASC";

    /// <summary>
    /// An ordering by the alias of a projected aggregate and then by a grouping key the
    /// projection does not carry, under a slice - the shape of LDBC IC 5. After the projection
    /// the second key would have nothing to name and the slice would pick other rows, so the
    /// whole ordering stands before the projection, over the group, the alias resolved to the
    /// aggregate it names. The provider of NHibernate 5.7.0 translates the chain
    /// (NHibernate/NHibernateLinqProviderTest), so the second form of NHibernate carries it too.
    /// </summary>
    [Fact]
    public void AGroupedQueryUnderASliceOrdersBeforeTheProjection()
    {
        var linq = Text(Sql(ORMEnum.EFCore, TopByCountThenKey));

        Assert.Contains(".GroupBy(s => new { s.CustomerId, s.Code })", linq, StringComparison.Ordinal);
        Assert.Contains(".OrderByDescending(g => g.Count()) .ThenBy(g => g.Key.Code) .Select(g => new { CustomerId = g.Key.CustomerId, Sold = g.Count() }) .Take(3)", linq, StringComparison.Ordinal);

        var nhibernate = Sql(ORMEnum.NHibernate, TopByCountThenKey);
        var forms = nhibernate.Build();
        var second = OneLine(forms.Single(s => s.ContentType == ConversionContentType.CSharpLinqQuery).Content);

        Assert.Contains(".OrderByDescending(g => g.Count()) .ThenBy(g => g.Key.Code) .Select(", second, StringComparison.Ordinal);
        Assert.DoesNotContain(nhibernate.Records, r => r.Kind is ConversionRecordKind.Omitted or ConversionRecordKind.Failure or ConversionRecordKind.Fallback);
    }

    /// <summary>
    /// The third level of the shape (decision 027): EF Core 10.0.10 translates an ordering
    /// between GroupBy and Select with an aggregate in its lambda, keeping both keys and the
    /// slice in the SQL it makes of the category of the matrix.
    /// </summary>
    [Fact]
    public void EFCoreTranslatesTheOrderingOverTheGroup()
    {
        var shape = QueryShapeInputs.Categories.Single(s => s.Name == "OrderingByAnUnprojectedKeyUnderASlice");
        var result = QueryShapeMatrixTest.Convert(shape, ORMEnum.Dapper, ORMEnum.EFCore);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "ExpressionVocabulary_OrderingOverTheGroup",
            result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using System;\nusing Microsoft.EntityFrameworkCore;\nusing Shop;");

        var sql = OneLine(EFCoreQueryAcceptance.Translate(compiled));

        Assert.Matches(@"SELECT TOP\(\S+\)", sql);
        Assert.Matches(@"ORDER BY COUNT\(\*\) DESC, \[\w+\]\.\[ProductId\]", sql);
    }

    /// <summary>
    /// Who speaks what of decision 113, as the probes against the pinned releases found it:
    /// T-SQL all of it, LINQ all but the window, HQL 7.4 all of it, HQL 5.7 grouping by an
    /// expression, ROUND, SQRT and CAST, EclipseLink's JPQL grouping by an expression, ROUND and
    /// SQRT. A fact about today, held here and not by the type.
    /// </summary>
    [Fact]
    public void EachDescriptorSpeaksWhatItsProbeFound()
    {
        static void Speaks(TargetFrameworkDescriptor descriptor, QueryFeature[] features, QueryFunction[] functions)
        {
            QueryFeature[] all = [QueryFeature.ComputedGrouping, QueryFeature.WindowFunction, QueryFeature.ListAggregation];
            QueryFunction[] added = [QueryFunction.DateAdd, QueryFunction.DateDiff, QueryFunction.Round, QueryFunction.Sqrt, QueryFunction.Cast];

            Assert.All(all, feature => Assert.Equal(
                features.Contains(feature) ? FactSupport.Expressible : FactSupport.NotExpressible,
                descriptor.SupportOf(feature)));
            Assert.All(added, function => Assert.Equal(functions.Contains(function), descriptor.Speaks(function)));
        }

        QueryFunction[] every = [QueryFunction.DateAdd, QueryFunction.DateDiff, QueryFunction.Round, QueryFunction.Sqrt, QueryFunction.Cast];

        Speaks(DapperDescriptor.Instance, [QueryFeature.ComputedGrouping, QueryFeature.WindowFunction, QueryFeature.ListAggregation], every);
        Speaks(MyBatisDescriptor.Instance, [QueryFeature.ComputedGrouping, QueryFeature.WindowFunction, QueryFeature.ListAggregation], every);
        Speaks(HibernateDescriptor.Instance, [QueryFeature.ComputedGrouping, QueryFeature.WindowFunction, QueryFeature.ListAggregation], every);
        Speaks(EFCoreDescriptor.Instance, [QueryFeature.ComputedGrouping, QueryFeature.ListAggregation], every);
        Speaks(NHibernateDescriptor.Instance, [QueryFeature.ComputedGrouping], [QueryFunction.Round, QueryFunction.Sqrt, QueryFunction.Cast]);
        Speaks(EclipseLinkDescriptor.Instance, [QueryFeature.ComputedGrouping], [QueryFunction.Round, QueryFunction.Sqrt]);
    }
}
