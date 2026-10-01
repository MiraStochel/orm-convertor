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
using MyBatisWrappers;
using NHibernateWrappers;
using Tests.Verification;

namespace Tests.Combined;

/// <summary>
/// The recursive definition of decision 113: an intermediate result of decision 112 whose
/// body is a UNION ALL of an anchor member and a recursive member that names the definition
/// itself, with the limit of recursion the query may carry. T-SQL reads and writes it, with
/// the limit as OPTION (MAXRECURSION n); HQL of Hibernate 7.4 reads and writes it without a
/// limit; EF Core, NHibernate and EclipseLink write the query in native SQL, and so does
/// Hibernate where the query limits its recursion. The rules of SQL Server's recursive
/// common table expression refuse in one place for every target, because the database would
/// refuse the query in any language. The starting value of a counter - 0 AS Depth - is a
/// constant under an alias, which the vocabulary carries for it. The categories of T2 carry
/// the shape through every direction and the fourth level (<c>QueryShapeMatrixTest</c>, the
/// differential matrix); this class names the rules.
/// </summary>
public class RecursionTest
{
    private static EntityMap Map(string entity, string table, params (string Name, ScalarType Type)[] columns)
    {
        var map = new EntityMap { Entity = new Entity { Name = entity }, Table = table };
        foreach (var (name, type) in columns)
        {
            var property = new Property { Name = name, Type = LangType.Scalar(type) };
            map.Entity.Properties.Add(property);
            map.PropertyMaps.Add(new PropertyMap { Property = property, ColumnName = name });
        }

        return map;
    }

    private static List<EntityMap> Maps() =>
    [
        Map("Category", "Categories", ("CategoryId", ScalarType.Int), ("ParentCategoryId", ScalarType.Int), ("Name", ScalarType.String)),
        Map("Link", "Links", ("FromId", ScalarType.Int), ("ToId", ScalarType.Int)),
    ];

    private static AbstractQueryBuilder FromSql(AbstractQueryBuilder builder, string sql)
    {
        builder.EntityMaps = Maps();
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql);
        return builder;
    }

    private static AbstractQueryBuilder FromHql(AbstractQueryBuilder builder, string hql)
    {
        builder.EntityMaps = Maps();
        new HibernateJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, hql, Maps());
        return builder;
    }

    private static List<ConversionSource> Built(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        return built;
    }

    private static string Artifact(List<ConversionSource> built, ConversionContentType type)
        => built.Single(s => s.ContentType == type).Content;

    private static string DapperSql(string sql) => Artifact(Built(FromSql(new DapperSqlQueryBuilder(), sql)), ConversionContentType.SqlQuery);

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

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

    private static void AssertRefused(AbstractQueryBuilder builder, QueryFeature feature, string named)
    {
        Assert.Empty(builder.Build());
        Assert.Contains(
            builder.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Feature == feature && r.Reason.Contains(named, StringComparison.Ordinal));
    }

    /// <summary>A descent of a hierarchy from its roots, with a counter of the depth: the anchor starts it, the recursive member adds one.</summary>
    private const string Descent = """
        WITH Tree AS (
            SELECT c.CategoryId AS CategoryId, c.Name AS Name, 0 AS Depth
            FROM Categories AS c
            WHERE c.ParentCategoryId IS NULL
            UNION ALL
            SELECT k.CategoryId, k.Name, t.Depth + 1
            FROM Categories AS k
            INNER JOIN Tree AS t ON k.ParentCategoryId = t.CategoryId)
        SELECT t.CategoryId AS CategoryId, t.Name AS Name, t.Depth AS Depth
        FROM Tree AS t
        """;

    /// <summary>A walk of a graph from one node, bounded by its number of steps, under a limit of recursion stated for the statement.</summary>
    private const string Walk = """
        WITH Walk AS (
            SELECT l.ToId AS NodeId, 1 AS Steps
            FROM Links AS l
            WHERE l.FromId = 1
            UNION ALL
            SELECT l.ToId, w.Steps + 1
            FROM Walk AS w
            INNER JOIN Links AS l ON l.FromId = w.NodeId
            WHERE w.Steps < 4)
        SELECT DISTINCT w.NodeId AS NodeId FROM Walk AS w
        OPTION (MAXRECURSION 10)
        """;

    // ---- reading T-SQL, writing T-SQL ------------------------------------------------

    /// <summary>
    /// The recursion reads into one definition and comes out as one; the recursive member is
    /// named by position after the anchor, which is where SQL takes the names from - the
    /// counter <c>t.Depth + 1</c> the source left without an alias comes out as Depth.
    /// </summary>
    [Fact]
    public void ARecursiveDefinitionRoundTripsInTSql()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), Descent);
        var sql = OneLine(Artifact(Built(builder), ConversionContentType.SqlQuery));

        Assert.StartsWith("WITH Tree AS ( SELECT c.CategoryId AS CategoryId, c.Name AS Name, 0 AS Depth FROM Categories AS c WHERE c.ParentCategoryId IS NULL", sql);
        Assert.Contains("UNION ALL SELECT k.CategoryId AS CategoryId, k.Name AS Name, t.Depth + 1 AS Depth FROM Categories AS k INNER JOIN Tree t ON k.ParentCategoryId = t.CategoryId )", sql);
        Assert.EndsWith("SELECT t.CategoryId AS CategoryId, t.Name AS Name, t.Depth AS Depth FROM Tree AS t", sql);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Failure or ConversionRecordKind.Loss or ConversionRecordKind.Fallback);
    }

    /// <summary>The limit closes the statement in both targets whose language is T-SQL: it decides whether the query finishes, so it is never dropped where it bounds something.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    public void TheLimitOfRecursionClosesTheStatement(ORMEnum target)
    {
        var builder = FromSql(Builder(target), Walk);
        var built = Built(builder);
        var text = target == ORMEnum.Dapper
            ? Artifact(built, ConversionContentType.SqlQuery)
            : Artifact(built, ConversionContentType.XML);

        Assert.Contains("SELECT DISTINCT w.NodeId AS NodeId FROM Walk AS w OPTION (MAXRECURSION 10)", OneLine(text), StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    /// <summary>A limit over a query without recursion bounds nothing: it is left out, with a record, and every other hint stays the loss it was.</summary>
    [Fact]
    public void ALimitWithoutRecursionIsLeftOut()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), "SELECT c.Name AS Name FROM Categories AS c OPTION (MAXRECURSION 5, RECOMPILE)");
        var sql = Artifact(Built(builder), ConversionContentType.SqlQuery);

        Assert.DoesNotContain("OPTION", sql, StringComparison.Ordinal);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Recursion && r.Reason.Contains("bounds nothing", StringComparison.Ordinal));
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("query hints were dropped", StringComparison.Ordinal));
    }

    /// <summary>A parameter of the anchor is typed from the column it is compared with, as anywhere else.</summary>
    [Fact]
    public void AParameterOfTheAnchorIsTypedFromItsColumn()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), Descent.Replace("c.ParentCategoryId IS NULL", "c.CategoryId = @root", StringComparison.Ordinal));
        var method = Artifact(Built(builder), ConversionContentType.CSharpQuery);

        Assert.Contains("int root", method, StringComparison.Ordinal);
    }

    // ---- HQL ------------------------------------------------------------------------

    /// <summary>Hibernate writes the recursion in its own language: a with clause whose body names itself, as verified against 7.4.5 over SQL Server.</summary>
    [Fact]
    public void HibernateWritesTheRecursionInHql()
    {
        var builder = FromSql(new HibernateJpqlQueryBuilder(), Descent);
        var hql = OneLine(Artifact(Built(builder), ConversionContentType.JpqlQuery));

        Assert.StartsWith("with Tree as ( select c.CategoryId as CategoryId, c.Name as Name, 0 as Depth from Category c where c.ParentCategoryId is null union all select k.CategoryId as CategoryId, k.Name as Name, t.Depth + 1 as Depth from Category k join Tree t on k.ParentCategoryId = t.CategoryId )", hql);
        Assert.EndsWith("select t.CategoryId as CategoryId, t.Name as Name, t.Depth as Depth from Tree t", hql);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Fallback or ConversionRecordKind.Failure);
    }

    /// <summary>What Hibernate writes reads back as the query it was written from: HQL's with that names itself is read like T-SQL's.</summary>
    [Fact]
    public void TheRecursionHibernateWritesReadsBack()
    {
        var hql = Artifact(Built(FromSql(new HibernateJpqlQueryBuilder(), Descent)), ConversionContentType.JpqlQuery);

        var reread = FromHql(new DapperSqlQueryBuilder(), hql);

        Assert.Equal(DapperSql(Descent), Artifact(Built(reread), ConversionContentType.SqlQuery));
    }

    /// <summary>HQL has recursion and no limit of it, so a query whose source states one goes to native SQL in Hibernate too.</summary>
    [Fact]
    public void HibernateFallsBackWhereTheQueryLimitsItsRecursion()
    {
        var builder = FromSql(new HibernateJpqlQueryBuilder(), Walk);
        var built = Built(builder);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Recursion && r.Reason.Contains("MAXRECURSION 10", StringComparison.Ordinal));
        Assert.Equal(DapperSql(Walk), Artifact(built, ConversionContentType.SqlQuery));
        Assert.Contains("em.createNativeQuery(", Artifact(built, ConversionContentType.JavaQuery), StringComparison.Ordinal);
    }

    /// <summary>The search and cycle clauses of HQL are refused by name: SQL Server has neither, and a cycle is guarded in the text of the query.</summary>
    [Theory]
    [InlineData("search depth first by CategoryId set ord")]
    [InlineData("cycle CategoryId set looped")]
    public void HqlSearchAndCycleAreRefused(string clause)
    {
        var hql = $"""
            with Tree as (
                select c.CategoryId as CategoryId from Category c where c.ParentCategoryId is null
                union all
                select k.CategoryId as CategoryId from Category k join Tree t on k.ParentCategoryId = t.CategoryId
            ) {clause}
            select t.CategoryId from Tree t
            """;

        var builder = FromHql(new DapperSqlQueryBuilder(), hql);

        Assert.Empty(builder.Build());
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("search or cycle clause", StringComparison.Ordinal));
    }

    // ---- the escape path -------------------------------------------------------------

    /// <summary>
    /// EF Core, NHibernate and EclipseLink have no recursion in their query language: each
    /// writes the query whole in native SQL - the text the Dapper target writes, limit
    /// included - with a record naming the recursion.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, Descent)]
    [InlineData(ORMEnum.EFCore, Walk)]
    [InlineData(ORMEnum.NHibernate, Descent)]
    [InlineData(ORMEnum.NHibernate, Walk)]
    [InlineData(ORMEnum.EclipseLink, Descent)]
    [InlineData(ORMEnum.EclipseLink, Walk)]
    public void ATargetWithoutRecursionWritesTheQueryInNativeSql(ORMEnum target, string sql)
    {
        var builder = FromSql(Builder(target), sql);
        var built = Built(builder);

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Fallback && r.Feature == QueryFeature.Recursion);
        Assert.Equal(DapperSql(sql), Artifact(built, ConversionContentType.SqlQuery));
    }

    /// <summary>
    /// What the escape path writes reads back as the same query, recursion and limit
    /// included (decision 113): the target's own reading of its own artifact gives the
    /// statement the Dapper target writes from the source.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, Descent)]
    [InlineData(ORMEnum.NHibernate, Walk)]
    [InlineData(ORMEnum.EclipseLink, Walk)]
    public void TheOutputOfTheEscapePathReadsBackAsTheSameQuery(ORMEnum target, string sql)
    {
        var built = Built(FromSql(Builder(target), sql));
        var method = Artifact(built, target == ORMEnum.EclipseLink ? ConversionContentType.JavaQuery : ConversionContentType.CSharpQuery);

        var reread = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        var read = target switch
        {
            ORMEnum.EFCore => new EFCoreLinqQueryParser(() => reread).Parse(ConversionContentType.CSharp, method, Maps()),
            ORMEnum.NHibernate => new NHibernateLinqQueryParser(() => reread).Parse(ConversionContentType.CSharp, method, Maps()),
            _ => new EclipseLinkJpqlQueryParser(() => reread).Parse(ConversionContentType.Java, method, Maps()),
        };

        Assert.Single(read);
        Assert.Equal(DapperSql(sql), Artifact(Built(reread), ConversionContentType.SqlQuery));
    }

    /// <summary>
    /// EF Core takes a native query that begins with WITH and ends with OPTION as long as
    /// nothing is composed over it: the method compiles in the consumer's frame and
    /// ToQueryString gives the text back unchanged (measured against 10.0.10, which runs it).
    /// </summary>
    [Fact]
    public void EFCoreTakesTheRecursiveQueryThroughSqlQuery()
    {
        var method = Artifact(Built(FromSql(new EFCoreLinqQueryBuilder(), Walk)), ConversionContentType.CSharpQuery);

        Assert.Contains("return ctx.Database.SqlQuery<QueryRow>(", method, StringComparison.Ordinal);

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "Recursion_EFCore_Row",
            method,
            [LinkEntity],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore;\nusing RecursionDomain;");

        var sql = EFCoreQueryAcceptance.Translate(compiled);
        Assert.StartsWith("WITH Walk AS (", sql, StringComparison.Ordinal);
        Assert.EndsWith("OPTION (MAXRECURSION 10)", sql.TrimEnd(), StringComparison.Ordinal);
    }

    private const string LinkEntity = """
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace RecursionDomain;

        [Table("Links")]
        [PrimaryKey(nameof(FromId), nameof(ToId))]
        public class Link
        {
            public int FromId { get; set; }

            public int ToId { get; set; }
        }
        """;

    // ---- the rules ------------------------------------------------------------------

    /// <summary>
    /// The rules of SQL Server's recursive common table expression, each refused by name for
    /// every target: the database would not run the query, in whatever language it came out.
    /// </summary>
    [Theory]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c INNER JOIN t ON t.Id = c.ParentCategoryId) SELECT t.Id FROM t", "no set operation")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c INNER JOIN t ON t.Id = c.ParentCategoryId UNION ALL SELECT c.CategoryId FROM Categories AS c) SELECT t.Id FROM t", "in its first member")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION SELECT k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "by Union")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId UNION ALL SELECT c.CategoryId FROM Categories AS c) SELECT t.Id FROM t", "anchor member after a recursive one")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT a.Id FROM t AS a INNER JOIN t AS b ON b.Id = a.Id) SELECT t.Id FROM t", "more than once")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT k.CategoryId FROM Categories AS k WHERE k.ParentCategoryId IN (SELECT t.Id FROM t)) SELECT t.Id FROM t", "inside a subquery")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT k.CategoryId FROM Categories AS k LEFT JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "left outer join")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT DISTINCT k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "DISTINCT")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT k.ParentCategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId GROUP BY k.ParentCategoryId) SELECT t.Id FROM t", "groups")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT MAX(k.CategoryId) FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "aggregates")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT TOP (5) k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "slices")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id FROM Categories AS c UNION ALL SELECT k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId WHERE k.CategoryId IN (SELECT l.ToId FROM Links AS l)) SELECT t.Id FROM t", "has a subquery")]
    [InlineData("WITH a AS (SELECT x.Id AS Id FROM b AS x), b AS (SELECT y.Id AS Id FROM a AS y) SELECT a.Id FROM a", "read each other")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id, c.Name AS Label FROM Categories AS c UNION ALL SELECT k.CategoryId, k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "is Int in its recursive member and String in its anchor member")]
    [InlineData("WITH t AS (SELECT c.CategoryId AS Id, c.Name AS Label FROM Categories AS c UNION ALL SELECT k.CategoryId FROM Categories AS k INNER JOIN t ON t.Id = k.ParentCategoryId) SELECT t.Id FROM t", "projects 1 columns where its anchor member projects 2")]
    public void ABrokenRuleOfRecursionRefusesInEveryTarget(string sql, string named)
    {
        foreach (var target in new[] { ORMEnum.Dapper, ORMEnum.MyBatis, ORMEnum.EFCore, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink })
        {
            var builder = FromSql(Builder(target), sql);

            AssertRefused(builder, QueryFeature.IntermediateResult, named);
            Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Fallback);
        }
    }

    // ---- the counter ----------------------------------------------------------------

    /// <summary>
    /// A constant under an alias is a column whose value every row shares - the starting
    /// depth of a recursion is one - and every target writes it as its language spells a
    /// projected literal: verified against NHibernate 5.7, EF Core 10, Hibernate 7.4 and
    /// EclipseLink 5.0, each of which returns it.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery, "0 AS Depth, 'x' AS Tag")]
    [InlineData(ORMEnum.MyBatis, ConversionContentType.XML, "0 AS Depth, 'x' AS Tag")]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharpQuery, "Depth = 0, Tag = \"x\"")]
    [InlineData(ORMEnum.NHibernate, ConversionContentType.HqlQuery, "0 as Depth, 'x' as Tag")]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.JpqlQuery, "0 as Depth, 'x' as Tag")]
    [InlineData(ORMEnum.EclipseLink, ConversionContentType.JpqlQuery, "0 as Depth, 'x' as Tag")]
    public void AConstantUnderAnAliasIsProjected(ORMEnum target, ConversionContentType artifact, string expected)
    {
        var builder = FromSql(Builder(target), "SELECT c.CategoryId AS CategoryId, 0 AS Depth, N'x' AS Tag FROM Categories AS c");
        var text = Artifact(Built(builder), artifact);

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain(builder.Records, r => r.Kind is ConversionRecordKind.Loss or ConversionRecordKind.Fallback);
    }

    /// <summary>The same column read from the other three languages that state it.</summary>
    [Fact]
    public void AConstantUnderAnAliasIsReadFromEveryLanguage()
    {
        var expected = DapperSql("SELECT c.CategoryId AS CategoryId, 0 AS Depth FROM Categories AS c");

        var linq = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        new EFCoreLinqQueryParser(() => linq).Parse(ConversionContentType.CSharp, "var q = ctx.Categories.Select(c => new { c.CategoryId, Depth = 0 }).ToList();", Maps());

        var hql = new DapperSqlQueryBuilder { EntityMaps = Maps() };
        new NHibernateHqlQueryParser(() => hql).Parse(ConversionContentType.HqlQuery, "select c.CategoryId as CategoryId, 0 as Depth from Category c", Maps());

        var jpql = FromHql(new DapperSqlQueryBuilder(), "select c.CategoryId as CategoryId, 0 as Depth from Category c");

        Assert.Equal(expected, Artifact(Built(linq), ConversionContentType.SqlQuery));
        Assert.Equal(expected, Artifact(Built(hql), ConversionContentType.SqlQuery));
        Assert.Equal(expected, Artifact(Built(jpql), ConversionContentType.SqlQuery));
    }

    /// <summary>A constant without an alias names no column any target could read it by, and stays the loss it was.</summary>
    [Fact]
    public void AConstantWithoutAnAliasIsDropped()
    {
        var builder = FromSql(new DapperSqlQueryBuilder(), "SELECT c.CategoryId AS CategoryId, 0 FROM Categories AS c");
        var sql = Artifact(Built(builder), ConversionContentType.SqlQuery);

        Assert.DoesNotContain(", 0", sql, StringComparison.Ordinal);
        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("constant without an alias", StringComparison.Ordinal));
    }
}
