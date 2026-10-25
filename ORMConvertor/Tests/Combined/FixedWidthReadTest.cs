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

namespace Tests.Combined;

/// <summary>
/// Decision 169: the value of a fixed-width column is the one SQL returns, padded to the width.
/// A target that trims it - EclipseLink - writes what the program receives of it as a
/// concatenation with empty text, and its entity nests a converter that pads it again; a
/// value a trimming source - EclipseLink - handed its program trimmed comes from every other
/// target trimmed, by RTRIM and its spellings in the query and by a converter in a Hibernate
/// entity, and the targets whose entity artifact has no place for it say so. Nothing but the
/// outermost select list is rewritten. What the frameworks do with the value is measured by
/// the Java suite's CharTrimmingClaimsTest and the provider tests of EF Core and NHibernate.
/// </summary>
public class FixedWidthReadTest
{
    private static EntityMap Flags(bool trimmed = false)
    {
        var id = new Property { Name = "FlagId", Type = LangType.Scalar(ScalarType.Int) };
        var code = new Property { Name = "Code", Type = LangType.Scalar(ScalarType.String, isNullable: true) };
        var wide = new Property { Name = "Wide", Type = LangType.Scalar(ScalarType.String, isNullable: true) };
        var note = new Property { Name = "Note", Type = LangType.Scalar(ScalarType.String, isNullable: true) };

        return new EntityMap
        {
            Entity = new Entity { Name = "Flag", Properties = [id, code, wide, note] },
            Table = "Flags",
            PropertyMaps =
            [
                new PropertyMap { Property = id, ColumnName = "FlagId" },
                new PropertyMap { Property = code, ColumnName = "Code", Type = DatabaseType.Char, Length = 5, IsUnicode = false, TrimmedOnRead = trimmed },
                new PropertyMap { Property = wide, ColumnName = "Wide", Type = DatabaseType.Char, Length = 5, IsUnicode = true, TrimmedOnRead = trimmed },
                new PropertyMap { Property = note, ColumnName = "Note", Type = DatabaseType.VarChar, Length = 10, IsUnicode = false, TrimmedOnRead = trimmed },
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

    /// <summary>A query of a source whose framework keeps the padding, read from T-SQL.</summary>
    private static AbstractQueryBuilder Sql(ORMEnum target, string sql, bool trimmed = false)
    {
        var builder = Builder(target);
        var map = Flags(trimmed);
        builder.EntityMaps = [map];
        new DapperSqlQueryParser(() => builder).Parse(ConversionContentType.SqlQuery, sql, [map]);
        return builder;
    }

    /// <summary>A query of an EclipseLink source, whose entity the EclipseLink reader marks as read trimmed.</summary>
    private static AbstractQueryBuilder EclipseLinkJpql(ORMEnum target, string jpql)
    {
        var builder = Builder(target);
        var map = Flags(trimmed: true);
        builder.EntityMaps = [map];
        new EclipseLinkJpqlQueryParser(() => builder).Parse(ConversionContentType.JpqlQuery, jpql, [map]);
        return builder;
    }

    private static (string Text, IReadOnlyList<ConversionRecord> Records) Built(AbstractQueryBuilder builder)
    {
        var built = builder.Build();
        Assert.True(
            built.Count > 0,
            "No artifact:\n" + string.Join("\n", builder.Records.Select(r => $"[{r.Kind}/{r.Feature}] {r.Reason}")));
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);

        var query = built.FirstOrDefault(s => s.ContentType is ConversionContentType.SqlQuery or ConversionContentType.HqlQuery or ConversionContentType.JpqlQuery or ConversionContentType.XML)
            ?? built.First();
        return (OneLine(query.Content), builder.Records);
    }

    private static string Text(AbstractQueryBuilder builder) => Built(builder).Text;

    private static string OneLine(string text) => string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    // ---- into EclipseLink ---------------------------------------------------------------

    /// <summary>
    /// The FIBEN q63 shape: a fixed-width column in the select list comes from EclipseLink as
    /// a concatenation with empty text under the name it had, a variable-width one bare, and
    /// a Convention says so.
    /// </summary>
    [Fact]
    public void EclipseLinkReadsAFixedWidthColumnAsAConcatenationWithEmptyText()
    {
        var (text, records) = Built(Sql(ORMEnum.EclipseLink, "SELECT f.Code, f.Note FROM Flags AS f"));

        Assert.Contains("select concat(f.Code, '') as Code, f.Note from Flag f", text, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Projection && r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SELECT f.Code AS Flagged FROM Flags AS f", "select concat(f.Code, '') as Flagged from Flag f")]
    [InlineData("SELECT f.Wide FROM Flags AS f", "select concat(f.Wide, '') as Wide from Flag f")]
    [InlineData("SELECT MAX(f.Code) AS Largest FROM Flags AS f", "select concat(max(f.Code), '') as Largest from Flag f")]
    [InlineData("SELECT MAX(f.Code) FROM Flags AS f", "select concat(max(f.Code), '') from Flag f")]
    [InlineData("SELECT COALESCE(f.Code, f.Note) AS Either FROM Flags AS f", "select concat(coalesce(f.Code, f.Note), '') as Either from Flag f")]
    [InlineData("SELECT f.Code, COUNT(*) AS Flagged FROM Flags AS f GROUP BY f.Code", "select concat(f.Code, '') as Code, count(f) as Flagged from Flag f group by f.Code")]
    public void EveryFixedWidthValueOfTheSelectListIsConcatenated(string sql, string expected)
    {
        Assert.Contains(expected, Text(Sql(ORMEnum.EclipseLink, sql)), StringComparison.Ordinal);
    }

    /// <summary>Only what the program receives: a condition, a subquery and a variable-width column stay as they were.</summary>
    [Theory]
    [InlineData("SELECT f.FlagId FROM Flags AS f WHERE f.Code = 'YES'", "select f.FlagId from Flag f where f.Code = 'YES'")]
    [InlineData("SELECT f.Note FROM Flags AS f WHERE f.Code IN (SELECT g.Code FROM Flags AS g)", "where f.Code in (select g.Code from Flag g)")]
    [InlineData("SELECT f.Note FROM Flags AS f", "select f.Note from Flag f")]
    public void NothingButTheSelectListIsRewritten(string sql, string expected)
    {
        var (text, records) = Built(Sql(ORMEnum.EclipseLink, sql));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain("concat", text, StringComparison.Ordinal);
        Assert.DoesNotContain(records, r => r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    /// <summary>Each member of a set operation is a select list the program receives.</summary>
    [Fact]
    public void EachMemberOfASetOperationIsRewritten()
    {
        var text = Text(Sql(ORMEnum.EclipseLink, "SELECT f.Code FROM Flags AS f WHERE f.FlagId = 1 UNION SELECT g.Code FROM Flags AS g WHERE g.FlagId = 2"));

        Assert.Equal(2, text.Split("concat(").Length - 1);
    }

    /// <summary>Under DISTINCT with an ordering by the same column the projection stays, and the target says what that costs.</summary>
    [Fact]
    public void ADistinctColumnThatIsAlsoTheOrderingStaysWithALoss()
    {
        var (text, records) = Built(Sql(ORMEnum.EclipseLink, "SELECT DISTINCT f.Code FROM Flags AS f ORDER BY f.Code"));

        Assert.DoesNotContain("concat", text, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Feature == QueryFeature.Projection && r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    /// <summary>A target that keeps the padding, from a source that kept it, changes nothing.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, "SELECT f.Code, f.Note FROM Flags AS f")]
    [InlineData(ORMEnum.MyBatis, "SELECT f.Code, f.Note FROM Flags AS f")]
    [InlineData(ORMEnum.Hibernate, "select f.Code, f.Note from Flag f")]
    [InlineData(ORMEnum.NHibernate, "select f.Code, f.Note from Flag f")]
    public void ATargetThatKeepsThePaddingWritesTheColumnBare(ORMEnum target, string expected)
    {
        var (text, records) = Built(Sql(target, "SELECT f.Code, f.Note FROM Flags AS f"));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.DoesNotContain(records, r => r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    // ---- out of EclipseLink -------------------------------------------------------------

    /// <summary>What an EclipseLink program received trimmed, every other target trims, each in its own spelling.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, "SELECT RTRIM(f.Code) AS Code, f.Note FROM Flags AS f")]
    [InlineData(ORMEnum.MyBatis, "SELECT RTRIM(f.Code) AS Code, f.Note FROM Flags AS f")]
    [InlineData(ORMEnum.Hibernate, "select trim(trailing from f.Code) as Code, f.Note from Flag f")]
    [InlineData(ORMEnum.NHibernate, "select trim(trailing from f.Code) as Code, f.Note from Flag f")]
    [InlineData(ORMEnum.EFCore, "Code = f.Code.TrimEnd()")]
    public void ATrimmedSourceIsTrimmedInEveryOtherTarget(ORMEnum target, string expected)
    {
        var (text, records) = Built(EclipseLinkJpql(target, "select f.Code, f.Note from Flag f"));

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Feature == QueryFeature.Projection && r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    [Fact]
    public void AMaximumOverATrimmedColumnIsTrimmed()
    {
        Assert.Contains("SELECT RTRIM(MAX(f.Code)) AS Largest FROM Flags AS f", Text(EclipseLinkJpql(ORMEnum.Dapper, "select max(f.Code) as Largest from Flag f")), StringComparison.Ordinal);
    }

    /// <summary>EclipseLink into EclipseLink is the same reading: nothing to rewrite.</summary>
    [Fact]
    public void EclipseLinkIntoEclipseLinkStaysAsItWas()
    {
        var (text, records) = Built(EclipseLinkJpql(ORMEnum.EclipseLink, "select f.Code, f.Note from Flag f"));

        Assert.Contains("select f.Code, f.Note from Flag f", text, StringComparison.Ordinal);
        Assert.DoesNotContain(records, r => r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    /// <summary>A concatenation the EclipseLink source wrote itself was padded there and stays padded everywhere.</summary>
    [Fact]
    public void AConcatenationOfTheSourceStaysPadded()
    {
        var text = Text(EclipseLinkJpql(ORMEnum.Dapper, "select concat(f.Code, '') as Code from Flag f"));

        Assert.DoesNotContain("RTRIM", text, StringComparison.Ordinal);
        Assert.Contains("f.Code + ''", text, StringComparison.Ordinal);
    }

    // ---- reading TrimEnd ----------------------------------------------------------------

    [Theory]
    [InlineData("SELECT RTRIM(f.Note) AS Note FROM Flags AS f", "select trim(trailing from f.Note) as Note from Flag f")]
    public void TSqlRtrimIsReadAsTrimEnd(string sql, string expected)
    {
        Assert.Contains(expected, Text(Sql(ORMEnum.Hibernate, sql)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("select trim(trailing from f.Note) as Note from Flag f", "SELECT RTRIM(f.Note) AS Note FROM Flags AS f")]
    [InlineData("select trim(trailing ' ' from f.Note) as Note from Flag f", "SELECT RTRIM(f.Note) AS Note FROM Flags AS f")]
    [InlineData("select trim(both from f.Note) as Note from Flag f", "SELECT TRIM(f.Note) AS Note FROM Flags AS f")]
    public void JpqlTrimmingIsRead(string jpql, string expected)
    {
        Assert.Contains(expected, Text(EclipseLinkJpql(ORMEnum.Dapper, jpql)), StringComparison.Ordinal);
    }

    /// <summary>A leading trim the vocabulary does not carry is named in the record of what was dropped.</summary>
    [Fact]
    public void JpqlLeadingTrimIsNamed()
    {
        var builder = EclipseLinkJpql(ORMEnum.Dapper, "select trim(leading from f.Note) as Note from Flag f");
        builder.Build();

        Assert.Contains(builder.Records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("trim(leading from", StringComparison.Ordinal));
    }

    [Fact]
    public void HqlTrailingTrimIsRead()
    {
        var builder = Builder(ORMEnum.Dapper);
        var map = Flags();
        builder.EntityMaps = [map];
        new NHibernateHqlQueryParser(() => builder).Parse(ConversionContentType.HqlQuery, "select trim(trailing from f.Note) as Note from Flag f", [map]);

        Assert.Contains("SELECT RTRIM(f.Note) AS Note FROM Flags AS f", Text(builder), StringComparison.Ordinal);
    }

    [Fact]
    public void LinqTrimEndIsRead()
    {
        var builder = Builder(ORMEnum.Dapper);
        var map = Flags();
        builder.EntityMaps = [map];
        var source = """
            public void Query()
            {
                var q = ctx.Flags.Select(f => new { Note = f.Note.TrimEnd() }).ToList();
            }
            """;
        new EFCoreLinqQueryParser(() => builder).Parse(ConversionContentType.CSharpQuery, source, [map]);

        Assert.Contains("RTRIM(f.Note) AS Note", Text(builder), StringComparison.Ordinal);
    }

    // ---- entities -----------------------------------------------------------------------

    private static (string Code, IReadOnlyList<ConversionRecord> Records) Entity(AbstractEntityBuilder builder, bool trimmed, bool keyIsFixedWidth = false)
    {
        builder.AddNamespace("Shop");
        builder.AddClassHeader("public", "Flag");
        builder.AddTable("Flags");
        builder.AddProperty(keyIsFixedWidth ? "string" : "int", "FlagId", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("string", "Code", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddProperty("string", "Wide", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddProperty("string", "Note", "public", hasGetter: true, hasSetter: true, isNullable: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Assigned, "FlagId");

        foreach (var propertyMap in builder.EntityMap.PropertyMaps)
        {
            var (type, unicode, length) = propertyMap.Property.Name switch
            {
                "FlagId" when keyIsFixedWidth => (DatabaseType.Char, false, 4),
                "FlagId" => ((DatabaseType?)null, (bool?)null, (int?)null),
                "Code" => (DatabaseType.Char, false, 5),
                "Wide" => (DatabaseType.Char, true, 5),
                _ => (DatabaseType.VarChar, false, 10),
            };
            propertyMap.Type = type;
            propertyMap.IsUnicode = unicode;
            propertyMap.Length = length;
            propertyMap.TrimmedOnRead = trimmed && propertyMap.Property.Type?.ScalarType == ScalarType.String;
        }

        var artifacts = builder.Build();
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        return (artifacts.First().Content, builder.Records);
    }

    /// <summary>
    /// The EclipseLink entity states the fixed width as a literal type and pads the value back
    /// with a nested converter per width; the record names the converter, the persistence
    /// unit and the limits of the padding.
    /// </summary>
    [Fact]
    public void AnEclipseLinkEntityPadsAFixedWidthValueBack()
    {
        var (code, records) = Entity(new EclipseLinkEntityBuilder(), trimmed: false);

        Assert.Contains("@Column(name = \"Code\", length = 5, columnDefinition = \"char(5)\")", code, StringComparison.Ordinal);
        Assert.Contains("@Column(name = \"Wide\", length = 5, columnDefinition = \"nchar(5)\")", code, StringComparison.Ordinal);
        Assert.Equal(2, code.Split("@Convert(converter = Flag.FixedWidth5.class)").Length - 1);
        Assert.Contains("public static class FixedWidth5 implements AttributeConverter<String, String> {", code, StringComparison.Ordinal);
        Assert.Contains("return value == null ? null : String.format(\"%-5s\", value);", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TrimmedFixedWidth", code, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Property == "Code" && r.Reason.Contains("persistence unit", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEclipseLinkEntityOfAnEclipseLinkSourceNeedsNoConverter()
    {
        var (code, _) = Entity(new EclipseLinkEntityBuilder(), trimmed: true);

        Assert.Contains("columnDefinition = \"char(5)\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("@Convert", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AHibernateEntityOfAnEclipseLinkSourceTrimsWithANestedConverter()
    {
        var (code, records) = Entity(new HibernateEntityBuilder(), trimmed: true);

        Assert.Contains("columnDefinition = \"char(5)\"", code, StringComparison.Ordinal);
        Assert.Contains("@Convert(converter = Flag.TrimmedFixedWidth.class)", code, StringComparison.Ordinal);
        Assert.Contains("while (end > 0 && value.charAt(end - 1) <= ' ') {", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FixedWidth5", code, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Property == "Code" && r.Reason.Contains("decision 169", StringComparison.Ordinal));
    }

    [Fact]
    public void AHibernateEntityOfAPaddingSourceNeedsNoConverter()
    {
        var (code, _) = Entity(new HibernateEntityBuilder(), trimmed: false);

        Assert.DoesNotContain("@Convert", code, StringComparison.Ordinal);
    }

    /// <summary>A fixed-width key takes no converter in Jakarta Persistence; the target says what that costs.</summary>
    [Fact]
    public void AFixedWidthKeyIsALoss()
    {
        var (code, records) = Entity(new EclipseLinkEntityBuilder(), trimmed: false, keyIsFixedWidth: true);

        Assert.DoesNotContain("Flag.FixedWidth4", code, StringComparison.Ordinal);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Property == "FlagId" && r.Reason.Contains("identifier", StringComparison.Ordinal));
    }

    /// <summary>A target whose entity artifact has no place for the trimming says so, once per column.</summary>
    [Theory]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    public void ATargetWithoutAPlaceForTheTrimmingSaysSo(ORMEnum target)
    {
        AbstractEntityBuilder builder = target switch
        {
            ORMEnum.EFCore => new EFCoreEntityBuilder(),
            ORMEnum.NHibernate => new NHibernateEntityBuilder(),
            ORMEnum.Dapper => new DapperEntityBuilder(),
            _ => new MyBatisEntityBuilder(),
        };

        var (_, records) = Entity(builder, trimmed: true);

        var losses = records.Where(r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("decision 169", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Code", "Wide"], losses.Select(r => r.Property).Order());
    }

    /// <summary>The EclipseLink reader marks every text property of the entity: its default session trims.</summary>
    [Fact]
    public void TheEclipseLinkReaderMarksTextAsReadTrimmed()
    {
        var builder = new HibernateEntityBuilder();
        var context = new JakartaPersistence.JpaReadingContext(EclipseLinkWrappers.EclipseLinkDescriptor.Profile);
        new EclipseLinkEntityParser(builder, context).Parse("""
            package Shop;

            import jakarta.persistence.*;

            @Entity
            @Table(name = "Flags")
            public class Flag {
                @Id
                @Column(name = "FlagId")
                private Integer flagId;

                @Column(name = "Code", columnDefinition = "char(5)")
                private String code;
            }
            """);

        var map = builder.EntityMaps.Single();
        Assert.True(map.PropertyMaps.Single(p => p.Property.Name == "code").TrimmedOnRead);
        Assert.False(map.PropertyMaps.Single(p => p.Property.Name == "flagId").TrimmedOnRead);
    }
}
