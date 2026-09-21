using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 over the character-type claims
/// NHibernate 5.7.0 has no exact registered name for: a fixed-length string other than a
/// single character and a non-unicode large text. The conversion table used to write
/// StringFixedLength, AnsiStringFixedLength and AnsiStringClob, which TypeFactory does
/// not register, so the session factory refused a mapping generated from a valid input;
/// the nearest registered name is written instead (decision 019).
///
/// Since decision 086 the claim the nearest name loses is carried by the second channel
/// the mapping has - the literal column type of the declared dialect - so what used to be
/// three loss records is three statements of the target, and the column says what the
/// source said. The last test of the class is the one that matters most: it is the only
/// level that shows NHibernate itself accepts a type attribute and a sql-type that differ.
/// </summary>
public class CharacterTypeVerificationTest
{
    private const string CountrySource = """
        namespace CharacterTypedEntities;

        using System.ComponentModel.DataAnnotations;

        public class Country
        {
            [Key]
            public int CountryID { get; set; }

            public string? IsoCode { get; set; }

            public string? PostalPrefix { get; set; }

            public string? Notes { get; set; }
        }
        """;

    private static NHibernateEntityBuilder Convert()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(CountrySource);

        // The claims a catalog or a source mapping would state: nchar(3), non-unicode
        // char(2), and a non-unicode text column.
        builder.SetPropertyDatabaseType("IsoCode", DatabaseType.Char, isUnicode: true, length: 3);
        builder.SetPropertyDatabaseType("PostalPrefix", DatabaseType.Char, isUnicode: false, length: 2);
        builder.SetPropertyDatabaseType("Notes", DatabaseType.Text, isUnicode: false);

        return builder;
    }

    [Fact]
    public void TheColumnCarriesWhatTheTypeNameCannot()
    {
        var builder = Convert();
        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("type=\"String\"", mapping);
        Assert.Contains("type=\"AnsiString\"", mapping);
        Assert.Contains("type=\"StringClob\"", mapping);
        Assert.DoesNotContain("FixedLength", mapping);
        Assert.DoesNotContain("AnsiStringClob", mapping);

        // The claim the registered name dropped, on the column, in the dialect the
        // descriptor declares (decision 086).
        Assert.Contains("length=\"3\" sql-type=\"nchar(3)\"", mapping);
        Assert.Contains("length=\"2\" sql-type=\"char(2)\"", mapping);
        Assert.Contains("sql-type=\"text\"", mapping);

        // Nothing is lost any more, and each of the three artifacts now names a type of
        // one database system, which the source did not - three statements of the target.
        var typeRecords = builder.Records
            .Where(r => r.Category == MappingFactCategory.DatabaseType)
            .ToList();

        Assert.Equal(3, typeRecords.Count);
        Assert.All(typeRecords, r => Assert.Equal(ConversionRecordKind.Convention, r.Kind));
        Assert.All(typeRecords, r => Assert.Contains(nameof(DatabaseDialect.SqlServer2022), r.Reason));
    }

    [Fact]
    public void AFixedLengthWithoutALengthKeepsTheNearestNameAndTheLoss()
    {
        // The dialect cannot spell this one: "nchar" alone is nchar(1) in T-SQL, so
        // deriving it would narrow the column to a single character silently. The nearest
        // registered name stands alone with the record it always had (decision 086).
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(CountrySource);
        builder.SetPropertyDatabaseType("IsoCode", DatabaseType.Char, isUnicode: true);

        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("name=\"IsoCode\"", mapping);
        Assert.DoesNotContain("sql-type", mapping);

        var record = Assert.Single(builder.Records, r => r.Category == MappingFactCategory.DatabaseType);
        Assert.Equal(ConversionRecordKind.Loss, record.Kind);
    }

    [Fact]
    public void ALiteralTypeOfTheSourceCarriesTheClaimAndReportsNothing()
    {
        // The source spelled the column itself, so nothing is derived and nothing is
        // claimed by us: the literal wins (decision 052) and it already says what the
        // registered name lost.
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(CountrySource);
        builder.SetPropertyDatabaseType(
            "IsoCode", DatabaseType.Char, isUnicode: true, sourceSqlType: "nchar(3)", length: 3);

        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("sql-type=\"nchar(3)\"", mapping);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.DatabaseType);
    }

    [Fact]
    public void ReadingTheOutputBackAndWritingItAgainChangesNothing()
    {
        // The statement is made once. Read back, the literal type is a fact of the source,
        // so the second pass repeats it, derives nothing and says nothing - and the
        // document is the same one (S2, decision 086).
        var outputs = Convert().Build();
        var entity = outputs.Single(o => o.ContentType == ConversionContentType.CSharpEntity).Content;
        var first = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;

        var again = new NHibernateEntityBuilder();
        new NHibernateEntityParser(again).Parse(entity);
        new NHibernateXMLMappingParser(again).Parse(first);
        var second = again.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Equal(first, second);
        Assert.DoesNotContain(again.Records, r => r.Category == MappingFactCategory.DatabaseType);
    }

    [Fact]
    public void GeneratedMappingIsValidAgainstTheSchema()
    {
        var mapping = Convert().Build().Single(o => o.ContentType == ConversionContentType.XML);

        var errors = NHibernateMappingSchema.Validate(mapping.Content);
        Assert.True(errors.Count == 0, "Generated mapping is invalid:"
            + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryFromTheArtifacts()
    {
        var outputs = Convert().Build();

        // The step that used to fail: TypeFactory cannot resolve an unregistered name,
        // so the binding of the mapping is where the invalid output surfaced. Since
        // decision 086 it also answers the question that decision had to ask - whether
        // 5.7.0 accepts a sql-type beside a type attribute that names another IType.
        NHibernateAcceptance.BuildSessionFactory(
            GeneratedEntityCompiler.CompileOrFail(
                "CharacterTypedEntities",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
