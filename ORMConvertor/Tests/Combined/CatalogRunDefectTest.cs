using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The translation defects the runs of the SEDE catalog found in the Java targets (decision
/// 208), each held over the shape that showed it rather than over SEDE: a column named
/// <c>Class</c>, whose getter would be <c>getClass()</c> (decision 212); a
/// <c>uniqueidentifier</c> column, for which MyBatis has no type handler (decision 210); and
/// a <c>#</c> in the native SQL of EclipseLink, which it reads as the mark of a named
/// parameter (decision 211), with what the measurement of that decision's limit found
/// beside it: the characters either JPA implementation reads as its own inside a bracketed
/// name (decision 217). One entity - stated by EF Core where a JPA entity needs its key,
/// by Dapper beside an SQL text -, converted without a catalog.
/// The facts each rule rests on are measured by the Java suite's
/// <c>mybatis/ResultMappingClaimsTest</c> and the <c>NativeSqlHashClaimsTest</c> and
/// <c>NativeSqlReadingClaimsTest</c> of <c>eclipselink</c> and <c>hibernate</c>.
/// </summary>
public class CatalogRunDefectTest
{
    /// <summary>The entity as Dapper states it: no key, which a MyBatis target and every query need not have.</summary>
    private const string Badge = """
        namespace Shop;

        public class Badge
        {
            public int Id { get; set; }

            public string? Name { get; set; }

            public byte? Class { get; set; }

            public Guid? RevisionGuid { get; set; }
        }
        """;

    /// <summary>The same entity as EF Core states it, with the key a JPA entity cannot be written without.</summary>
    private const string KeyedBadge = """
        using System;
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Shop;

        [Table("Badges")]
        public class Badge
        {
            [Key]
            public int Id { get; set; }

            public string? Name { get; set; }

            public byte? Class { get; set; }

            public Guid? RevisionGuid { get; set; }
        }
        """;

    private static ConversionResult Convert(ORMEnum target, string? sql = null)
        => ConversionHandler.Convert(
            sql is null ? ORMEnum.EFCore : ORMEnum.Dapper,
            target,
            [
                new ConversionSource { Name = "Badge.cs", ContentType = ConversionContentType.CSharp, Content = sql is null ? KeyedBadge : Badge },
                .. sql is null ? [] : new[] { new ConversionSource { Name = "Query.sql", ContentType = ConversionContentType.SqlQuery, Content = sql } },
            ]);

    private static string Entity(ConversionResult result)
        => Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity).Content;

    private static string EntityMapper(ConversionResult result)
        => Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.XML && s.Content.Contains("<resultMap", StringComparison.Ordinal)).Content;

    /// <summary>The text of every query artifact, each run of whitespace one space.</summary>
    private static string Query(ConversionResult result)
        => Regex.Replace(string.Join("\n", result.Sources.Where(s => s.ContentType.IsQuery() || s.Content.Contains("<select", StringComparison.Ordinal)).Select(s => s.Content)), @"\s+", " ");

    /* ---- a property whose getter would be getClass() ---------------------------------- */

    /// <summary>
    /// Object.getClass() is final, so a getter of that name is a class javac refuses: the
    /// member takes a trailing underscore and the column keeps its name in the mapping.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    [InlineData(ORMEnum.MyBatis)]
    public void APropertyNamedClassIsAMemberWithAnUnderscore(ORMEnum target)
    {
        var result = Convert(target);
        var entity = Entity(result);

        Assert.Contains("private Byte Class_;", entity);
        Assert.Contains("public Byte getClass_() {", entity);
        Assert.Contains("public void setClass_(Byte value) {", entity);
        Assert.DoesNotContain("getClass()", entity);
        Assert.Contains("private String Name;", entity);

        var renamed = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Convention && r.Property == "Class");
        Assert.Contains("'Class_'", renamed.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Convention && r.Property == "Name");
    }

    [Fact]
    public void TheJpaColumnOfTheRenamedMemberIsNamed()
    {
        Assert.Contains("@Column(name = \"Class\"", Entity(Convert(ORMEnum.Hibernate)));
        Assert.Contains("@Column(name = \"Class\"", Entity(Convert(ORMEnum.EclipseLink)));
    }

    [Fact]
    public void TheMyBatisColumnOfTheRenamedMemberIsNamed()
        => Assert.Contains("<result column=\"Class\" property=\"Class_\"", EntityMapper(Convert(ORMEnum.MyBatis)));

    /// <summary>JPQL names the member, so the query names it as the entity does.</summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void JpqlNamesTheRenamedMember(ORMEnum target)
    {
        var query = Query(Convert(target, "SELECT b.Name, b.Class FROM Badges b WHERE b.Class > 1 ORDER BY b.Class"));

        Assert.Contains("select b.Name, b.Class_ from Badge b where b.Class_ > 1 order by b.Class_ asc", query);
    }

    /* ---- a MyBatis query materializes an entity by its own <resultMap> ---------------- */

    /// <summary>
    /// Under a resultType MyBatis maps the column Class onto no property, since the member is
    /// Class_; the entity's own result map names every column, so the query points at it.
    /// </summary>
    [Fact]
    public void AMyBatisQueryOverTheWholeEntityTakesItsResultMap()
    {
        var result = Convert(ORMEnum.MyBatis, "SELECT * FROM Badges WHERE Class = 1");

        Assert.Contains("<select id=\"query\" resultMap=\"Shop.BadgeMapper.Badge\">", Query(result));
        Assert.Contains("<mapper namespace=\"Shop.BadgeMapper\">", EntityMapper(result));
        Assert.Contains("<resultMap id=\"Badge\" type=\"Shop.Badge\" autoMapping=\"false\">", EntityMapper(result));
        Assert.Contains("List<Badge> query();", Query(result));
    }

    /// <summary>A table no entity of the conversion maps has no mapper to point at; its derived type stays a resultType.</summary>
    [Fact]
    public void AMyBatisQueryOverATableWithoutAnEntityKeepsTheResultType()
        => Assert.Contains("<select id=\"query\" resultType=\"Shop.Medal\">", Query(Convert(ORMEnum.MyBatis, "SELECT * FROM Medals")));

    /* ---- a uniqueidentifier in MyBatis ------------------------------------------------ */

    /// <summary>
    /// MyBatis has no type handler for java.util.UUID: the class carries the value as the text
    /// the driver hands over, and says so.
    /// </summary>
    [Fact]
    public void AGuidIsAStringInMyBatis()
    {
        var result = Convert(ORMEnum.MyBatis);
        var entity = Entity(result);

        Assert.Contains("private String RevisionGuid;", entity);
        Assert.Contains("public String getRevisionGuid() {", entity);
        Assert.DoesNotContain("UUID", entity);
        Assert.Contains("<result column=\"RevisionGuid\" property=\"RevisionGuid\"", EntityMapper(result));

        var loss = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "RevisionGuid" && r.Artifact == ConversionContentType.JavaEntity);
        Assert.Contains("java.util.UUID", loss.Reason, StringComparison.Ordinal);
    }

    /// <summary>The JPA implementations map UUID themselves.</summary>
    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AGuidStaysAUuidInJpa(ORMEnum target)
    {
        var result = Convert(target);

        Assert.Contains("private UUID RevisionGuid;", Entity(result));
        Assert.DoesNotContain(result.Records, r => r.Property == "RevisionGuid" && r.Kind == ConversionRecordKind.Loss);
    }

    /* ---- a # in the native SQL of EclipseLink ----------------------------------------- */

    private const string HashAlias = "SELECT b.Name AS [#] FROM Badges b";

    /// <summary>
    /// The alias [#] is no member name, so both JPA targets write the query in native SQL;
    /// EclipseLink is told to read another mark of named parameters, one the text does not hold.
    /// </summary>
    [Fact]
    public void ANativeQueryOfEclipseLinkWithAHashNamesAnotherParameterMark()
    {
        var result = Convert(ORMEnum.EclipseLink, HashAlias);
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Fallback);
        Assert.Contains("SELECT b.Name AS [#]", method);
        Assert.Contains("\"\"\")\n        .setHint(QueryHints.PARAMETER_DELIMITER, \"\\u0001\");", method.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheMarkIsAControlCharacterTheTextDoesNotHold()
    {
        var method = Assert.Single(
            Convert(ORMEnum.EclipseLink, "SELECT b.Name AS [#] FROM Badges b WHERE b.Name <> '\u0001'").Sources,
            s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Contains(".setHint(QueryHints.PARAMETER_DELIMITER, \"\\u0002\")", method);
    }

    [Fact]
    public void ANativeQueryWithoutAHashCarriesNoHint()
    {
        var method = Assert.Single(
            Convert(ORMEnum.EclipseLink, "SELECT b.Name AS [a b] FROM Badges b").Sources,
            s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Contains("createNativeQuery", method);
        Assert.DoesNotContain("setHint", method);
    }

    /// <summary>Hibernate reads no such mark in native SQL, so its query carries none.</summary>
    [Fact]
    public void HibernateWritesTheHashAsItStands()
    {
        var method = Assert.Single(Convert(ORMEnum.Hibernate, HashAlias).Sources, s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.Contains("SELECT b.Name AS [#]", method);
        Assert.DoesNotContain("setHint", method);
    }

    /* ---- a character a JPA implementation reads inside a bracketed name ---------------- */

    private const string ReadNames = @"SELECT b.Name AS [a?b], b.Id AS [c:d], b.Class AS [it's], b.RevisionGuid AS [e\f] FROM Badge b WHERE b.Id = @id AND b.Name <> 'x?y'";

    /// <summary>
    /// Hibernate does not know the brackets of T-SQL and reads a ?, a colon, a backslash and a
    /// quote inside them as its own; a backslash before each passes it as text (decision 217).
    /// The text block of Java doubles the backslash, the literal and the bare SQL stand as
    /// they were, and the parameter after the apostrophe is still a parameter.
    /// </summary>
    [Fact]
    public void HibernateWritesWhatItReadsInABracketedNameBehindABackslash()
    {
        var result = Convert(ORMEnum.Hibernate, ReadNames);
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaQuery).Content;
        var sql = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.SqlQuery).Content;

        Assert.DoesNotContain(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Feature: not null });
        Assert.Contains(@"AS [a\\?b]", method);
        Assert.Contains(@"AS [c\\:d]", method);
        Assert.Contains(@"AS [it\\'s]", method);
        Assert.Contains(@"AS [e\\\\f]", method);
        Assert.Contains("b.Id = ?1", method);
        Assert.Contains("'x?y'", method);
        Assert.Contains(".setParameter(1, id)", method);

        Assert.Contains("AS [a?b]", sql);
        Assert.Contains(@"AS [e\f]", sql);
    }

    /// <summary>
    /// EclipseLink reads a ? inside a bracketed name as a positional parameter and has no
    /// spelling that keeps it text, so the query is refused with a record that names the name
    /// instead of going out as a method that fails when it runs.
    /// </summary>
    [Fact]
    public void EclipseLinkRefusesAQuestionMarkInABracketedName()
    {
        var result = Convert(ORMEnum.EclipseLink, "SELECT b.Name AS [a?b] FROM Badges b");

        Assert.DoesNotContain(result.Sources, s => s.ContentType is ConversionContentType.JavaQuery or ConversionContentType.SqlQuery);
        var failure = Assert.Single(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Feature: AbstractWrappers.Descriptors.QueryFeature.Projection });
        Assert.Contains("[a?b]", failure.Reason);
        Assert.Contains("no artifact was generated", failure.Reason);
    }

    /// <summary>What EclipseLink reads as text in a bracketed name goes out as it stands, and a ? in a literal is no matter of a name.</summary>
    [Fact]
    public void EclipseLinkWritesTheOtherCharactersOfABracketedNameAsTheyStand()
    {
        var result = Convert(ORMEnum.EclipseLink, @"SELECT b.Id AS [c:d], b.Class AS [it's], b.RevisionGuid AS [e\f] FROM Badge b WHERE b.Id = @id AND b.Name <> 'x?y'");
        var method = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaQuery).Content;

        Assert.DoesNotContain(result.Records, r => r is { Kind: ConversionRecordKind.Failure, Feature: not null });
        Assert.Contains("AS [c:d]", method);
        Assert.Contains("AS [it's]", method);
        Assert.Contains(@"AS [e\\f]", method);
        Assert.Contains("b.Id = ?1", method);
        Assert.Contains("'x?y'", method);
    }

    /// <summary>
    /// The respelling of placeholders reads a bracketed name as a name: the apostrophe in it
    /// opens no literal, so the parameter after it is respelled, and an @ in it is no placeholder.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.MyBatis, "#{id}")]
    [InlineData(ORMEnum.EclipseLink, "?1")]
    public void AParameterAfterAnApostropheInABracketedNameIsRespelled(ORMEnum target, string placeholder)
    {
        var query = Query(Convert(target, "SELECT b.Name AS [it's @id] FROM Badge b WHERE b.Id = @id"));

        Assert.Contains("AS [it's @id]", query);
        Assert.Contains($"b.Id = {placeholder}", query);
    }
}
