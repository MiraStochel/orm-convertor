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
/// now the nearest registered name is written and the changed claim is a loss record
/// (decision 019).
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

        // The claims a catalog or a source mapping would state: char(3), non-unicode
        // char(2), and a non-unicode text column.
        builder.SetPropertyDatabaseType("IsoCode", DatabaseType.Char, isUnicode: true, length: 3);
        builder.SetPropertyDatabaseType("PostalPrefix", DatabaseType.Char, isUnicode: false, length: 2);
        builder.SetPropertyDatabaseType("Notes", DatabaseType.Text, isUnicode: false);

        return builder;
    }

    [Fact]
    public void RegisteredNamesAreWrittenAndTheNarrowingIsReported()
    {
        var builder = Convert();
        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        Assert.Contains("type=\"String\" length=\"3\"", mapping);
        Assert.Contains("type=\"AnsiString\" length=\"2\"", mapping);
        Assert.Contains("type=\"StringClob\"", mapping);
        Assert.DoesNotContain("FixedLength", mapping);
        Assert.DoesNotContain("AnsiStringClob", mapping);

        // Each substituted name changes the claim - fixedness twice, the unicode facet
        // once - and each change is one loss record from the point of emission.
        Assert.Equal(3, builder.Records.Count(r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.DatabaseType));
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
        // so the binding of the mapping is where the invalid output surfaced.
        NHibernateAcceptance.BuildSessionFactory(
            GeneratedEntityCompiler.CompileOrFail(
                "CharacterTypedEntities",
                outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
                GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }
}
