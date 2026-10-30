using System.Diagnostics;
using System.Text;
using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Microsoft.Extensions.Configuration;
using Model;
using OrmConvertor;
using ORMConvertorAPI;
using Tests.Experiment;

namespace Tests.Combined;

/// <summary>
/// The cap on the text of one query (decision 194), at each of the places that hold it. Two
/// spellings of decision 156 write an operand twice - the LINQ writer tests the operand of a
/// comparison for NULL beside the comparison, the reader of EF Core reads a compensated
/// <c>!=</c> as <c>&lt;&gt; OR IS NULL</c> - and the include of MyBatis inlines a fragment as
/// many times as it is named, so an input of a kilobyte writes megabytes within a dozen levels.
/// Measured 2026-10-30: a CASE compared <c>&lt;&gt;</c> in the condition of a CASE, 14 deep,
/// writes 1.6 million characters of LINQ from 840 of T-SQL; the same shape written once per
/// level in C# writes 35 million of T-SQL at 18 levels and 130 million of LINQ at 13.
///
/// The suite holds the boundary with a small cap, so that no test writes megabytes, and holds
/// the default cap once per place with the shape that crosses it, so that the text the cap is
/// there to stop is seen stopping.
/// </summary>
public class QueryTextCapTest
{
    private const int SmallCap = 4096;

    private static readonly ParseLimits Small = new() { MaxQueryTextLength = SmallCap };

    private static string Schema => QueryShapeInputs.Schema;

    /// <summary>ParentDepartmentId is nullable in every mapping of the fixture, so the LINQ writer tests it for NULL.</summary>
    private static List<ConversionSource> CaseComparedInSql(int depth)
    {
        var condition = "d.ParentDepartmentId > 0";
        for (var i = 1; i <= depth; i++)
        {
            condition = $"CASE WHEN {condition} THEN d.ParentDepartmentId ELSE 0 END <> 1";
        }

        return
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.Dapper),
            new() { Name = "FindNested.sql", ContentType = ConversionContentType.SqlQuery, Content = $"SELECT d.DepartmentId AS DepartmentId FROM {Schema}.ShopDepartments AS d WHERE {condition}" },
        ];
    }

    private static List<ConversionSource> CaseComparedInHql(int depth)
    {
        var condition = "d.ParentDepartmentId > 0";
        for (var i = 1; i <= depth; i++)
        {
            condition = $"case when {condition} then d.ParentDepartmentId else 0 end <> 1";
        }

        return
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.NHibernate),
            new() { Name = "FindNested.hql", ContentType = ConversionContentType.HqlQuery, Content = $"select d.DepartmentId as DepartmentId from ShopDepartment d where {condition}" },
        ];
    }

    /// <summary>The operand written once per level, as a C# author writes it.</summary>
    private static List<ConversionSource> CaseComparedInLinq(int depth)
    {
        var predicate = "d.ParentDepartmentId > 0";
        for (var i = 1; i <= depth; i++)
        {
            predicate = $"({predicate} ? d.ParentDepartmentId : 0) != 1";
        }

        return
        [
            .. QueryShapeInputs.MappingUnits(ORMEnum.EFCore),
            new()
            {
                Name = "FindNested.query.cs",
                ContentType = ConversionContentType.CSharp,
                Content = $"public void Query()\n{{\n    var q = ctx.ShopDepartments.Where(d => {predicate}).Select(d => new {{ DepartmentId = d.DepartmentId }})\n        .ToList();\n}}",
            },
        ];
    }

    /// <summary>A fragment that inlines the one below it twice, into a flat list of values, so that no depth grows with the text.</summary>
    private static List<ConversionSource> DoublingInclude(int depth)
    {
        var fragments = new StringBuilder("  <sql id=\"f0\">1</sql>\n");
        for (var i = 1; i <= depth; i++)
        {
            fragments.Append($"  <sql id=\"f{i}\"><include refid=\"f{i - 1}\"/>, <include refid=\"f{i - 1}\"/></sql>\n");
        }

        return
        [
            new()
            {
                Name = "ShopProduct.java",
                ContentType = ConversionContentType.Java,
                Content = "package Shop;\n\npublic class ShopProduct {\n    private Integer ProductId;\n    private String ProductName;\n}\n",
            },
            new()
            {
                Name = "ShopProductMapper.xml",
                ContentType = ConversionContentType.XML,
                Content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                    + "<!DOCTYPE mapper PUBLIC \"-//mybatis.org//DTD Mapper 3.0//EN\" \"https://mybatis.org/dtd/mybatis-3-mapper.dtd\">\n"
                    + "<mapper namespace=\"Shop.ShopProductMapper\">\n"
                    + fragments
                    + $"  <select id=\"findSome\" resultType=\"ShopProduct\">SELECT p.ProductName AS ProductName FROM {Schema}.ShopProducts AS p WHERE p.ProductId IN (<include refid=\"f{depth}\"/>)</select>\n"
                    + "</mapper>\n",
            },
        ];
    }

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, List<ConversionSource> units, ParseLimits? limits)
        => ConversionHandler.Convert(source, target, units, catalogConnectionString: null, declaredSourceDialect: null, limits);

    private static ConversionRecord? CapRefusal(ConversionResult result, int cap, ConversionRecordKind kind = ConversionRecordKind.Failure)
        => result.Records.FirstOrDefault(r => r.Kind == kind && r.Reason.Contains($"than the {cap} characters of one query this instance writes", StringComparison.Ordinal));

    private static ConversionSource? Query(ConversionResult result)
        => result.Sources.FirstOrDefault(s => s.ContentType.IsQuery());

    /* ---- the LINQ writer: the operand of a comparison tested for NULL ---------------------- */

    [Theory]
    [InlineData(4, false)]
    [InlineData(6, true)]
    [InlineData(9, true)]
    public void ACaseComparedInsideACaseIsWrittenInLinqUpToTheCap(int depth, bool refused)
    {
        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(depth), Small);

        if (!refused)
        {
            Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
            Assert.NotNull(Query(result));
            return;
        }

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Same(refusal, CapRefusal(result, SmallCap));
        Assert.Null(Query(result));
    }

    /// <summary>The same shape, in HQL, into NHibernate: the binding form is linear in the depth and stands, the LINQ form crosses the cap and is omitted.</summary>
    [Fact]
    public void TheLinqFormOfNHibernateIsOmittedWhileItsHqlStands()
    {
        var result = Convert(ORMEnum.NHibernate, ORMEnum.NHibernate, CaseComparedInHql(7), Small);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.NotNull(CapRefusal(result, SmallCap, ConversionRecordKind.Omitted));
        Assert.Contains(result.Sources, s => s.ContentType == ConversionContentType.HqlQuery);
        Assert.DoesNotContain(result.Sources, s => s.ContentType == ConversionContentType.CSharpLinqQuery);
    }

    /// <summary>At the default cap the shape is refused where it crosses a megabyte, within milliseconds, and translated one level below.</summary>
    [Fact]
    public void TheDefaultCapStopsTheDoublingOfTheLinqWriterAtAMegabyte()
    {
        var below = Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(13), null);
        Assert.DoesNotContain(below.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.InRange(Query(below)!.Content.Length, 500_000, ParseLimits.DefaultMaxQueryTextLength);

        var watch = Stopwatch.StartNew();
        var crossing = Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(14), null);
        watch.Stop();

        Assert.NotNull(CapRefusal(crossing, ParseLimits.DefaultMaxQueryTextLength));
        Assert.Null(Query(crossing));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Refusing the shape took {watch.Elapsed}.");
    }

    /* ---- the reader of EF Core: the model counted as written -------------------------------- */

    /// <summary>
    /// Eight levels write 34 thousand characters of T-SQL from a model of some fifteen hundred
    /// nodes as written, so the finished text crosses the small cap first; eleven levels are twelve
    /// thousand nodes as written, which the count after the reading refuses before any walk.
    /// </summary>
    [Theory]
    [InlineData(4, false, false)]
    [InlineData(8, true, false)]
    [InlineData(11, true, true)]
    public void TheModelOfACompensatedInequalityIsCountedAsWritten(int depth, bool refused, bool byTheModel)
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, CaseComparedInLinq(depth), Small);

        if (!refused)
        {
            Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
            Assert.NotNull(Query(result));
            return;
        }

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains($"{SmallCap} characters", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal(byTheModel, refusal.Reason.Contains("nodes as written", StringComparison.Ordinal));
        Assert.Null(Query(result));
    }

    /// <summary>
    /// Thirty levels, where the text would be a thousand times the memory of the machine: the
    /// model is a graph of thirty shared operands, counted as written in thirty steps, and refused
    /// before any walk descends into it.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.Hibernate)]
    public void TheDefaultCapRefusesTheSharedOperandBeforeAnyWalk(ORMEnum target)
    {
        var watch = Stopwatch.StartNew();
        var result = Convert(ORMEnum.EFCore, target, CaseComparedInLinq(30), null);
        watch.Stop();

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Contains("nodes as written", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(Query(result));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Refusing the shape into {target} took {watch.Elapsed}.");
    }

    /* ---- the include of MyBatis ------------------------------------------------------------- */

    [Theory]
    [InlineData(10, false)]
    [InlineData(12, true)]
    public void AnIncludeThatInlinesAFragmentTwiceIsExpandedUpToTheCap(int depth, bool refused)
    {
        var result = Convert(ORMEnum.MyBatis, ORMEnum.Dapper, DoublingInclude(depth), Small);

        if (!refused)
        {
            Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
            Assert.NotNull(Query(result));
            return;
        }

        var refusal = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Same(refusal, CapRefusal(result, SmallCap));
        Assert.Contains("<include refid=\"f", refusal.Reason, StringComparison.Ordinal);
        Assert.Null(Query(result));
    }

    /// <summary>Twenty levels of fragments expand to three million characters; the cap stops the expansion while it is still kilobytes.</summary>
    [Fact]
    public void TheDefaultCapStopsTheExpansionOfIncludesAtAMegabyte()
    {
        var watch = Stopwatch.StartNew();
        var result = Convert(ORMEnum.MyBatis, ORMEnum.Dapper, DoublingInclude(20), null);
        watch.Stop();

        Assert.NotNull(CapRefusal(result, ParseLimits.DefaultMaxQueryTextLength));
        Assert.Null(Query(result));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Refusing the expansion took {watch.Elapsed}.");
    }

    /* ---- the finished artifact and the switch ----------------------------------------------- */

    /// <summary>A flat filter of many conditions multiplies nothing and crosses a small cap only as a finished text; the template refuses it whatever wrote it.</summary>
    [Fact]
    public void AFinishedArtifactLongerThanTheCapIsRefusedWhateverWroteIt()
    {
        var limits = new ParseLimits { MaxQueryTextLength = 2048 };
        var units = LimitSeries.Axis("conditions")!.Units(ORMEnum.Dapper, 256);

        foreach (var target in new[] { ORMEnum.Dapper, ORMEnum.EFCore })
        {
            var result = Convert(ORMEnum.Dapper, target, units, limits);

            Assert.NotNull(CapRefusal(result, 2048));
            Assert.Null(Query(result));
        }
    }

    [Fact]
    public void AnOperatorWhoSwitchesTheCapOffGetsTheWholeText()
    {
        var limits = new ParseLimits { MaxQueryTextLength = ParseLimits.Unlimited };

        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(8), limits);

        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.True(Query(result)!.Content.Length > SmallCap);
        Assert.Equal(ParseLimits.Unlimited, result.MaxQueryTextLength);
    }

    [Fact]
    public void TheRunRecordCarriesTheCapTheQueriesWereWrittenUnder()
    {
        Assert.Equal(SmallCap, Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(1), Small).MaxQueryTextLength);
        Assert.Equal(ParseLimits.DefaultMaxQueryTextLength, Convert(ORMEnum.Dapper, ORMEnum.EFCore, CaseComparedInSql(1), null).MaxQueryTextLength);
    }

    [Fact]
    public void TheConfigurationReadsBothCapsAndDefaultsEachAlone()
    {
        var both = ParseLimitsConfiguration.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ParseLimitsConfiguration.Key] = "64", [ParseLimitsConfiguration.QueryTextKey] = "1000" })
            .Build());
        Assert.Equal(64, both.MaxNestingDepth);
        Assert.Equal(1000, both.MaxQueryTextLength);

        var depthOnly = ParseLimitsConfiguration.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ParseLimitsConfiguration.Key] = "0" })
            .Build());
        Assert.False(depthOnly.CapsNesting);
        Assert.Equal(ParseLimits.DefaultMaxQueryTextLength, depthOnly.MaxQueryTextLength);

        Assert.Equal(ParseLimits.Default, ParseLimitsConfiguration.Read(new ConfigurationBuilder().Build()));
    }
}
