using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The version column as a mapping fact of its own (decision 030): a flag in the model
/// rather than a database type, expressed by EF Core as [Timestamp] where the database
/// produces the value and as [ConcurrencyCheck] with a loss record where the source's
/// framework increments it, as the version element by NHibernate, and inexpressible in
/// Dapper, where the mechanical loss record states a property of Dapper rather than of
/// the tool. The second half, decision 116: [ConcurrencyCheck] read back as the version
/// the application keeps, exact in EF Core and a convention record where NHibernate or a
/// JPA provider takes the increment over.
/// </summary>
public class VersionColumnTest
{
    private const string NHibernateDocument = """
        namespace Library;

        public class Document
        {
            public virtual int DocumentID { get; set; }

            public virtual int Revision { get; set; }

            public virtual string? Title { get; set; }
        }
        """;

    private const string NHibernateDocumentMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Library">
            <class name="Document" table="Documents">
                <id name="DocumentID" type="Int32">
                    <generator class="assigned" />
                </id>
                <version name="Revision" type="Int32" />
                <property name="Title" />
            </class>
        </hibernate-mapping>
        """;

    private const string JpaDocument = """
        package Library;

        import jakarta.persistence.*;

        @Entity
        @Table(name = "Documents")
        public class Document {
            @Id
            private Integer DocumentID;

            @Version
            private int Revision;

            private String Title;
        }
        """;

    /// <summary>
    /// The two sources whose framework increments a numeric version itself: NHibernate's
    /// version element over Int32, with the family stated, and JPA's @Version int, without.
    /// </summary>
    public static TheoryData<ORMEnum> FrameworkIncrementedSources() => new(ORMEnum.NHibernate, ORMEnum.Hibernate);

    public static ConversionResult ConvertNumericVersion(ORMEnum source, ORMEnum target)
        => ConversionHandler.Convert(source, target, source == ORMEnum.NHibernate
            ?
            [
                new ConversionSource { Content = NHibernateDocument, ContentType = ConversionContentType.CSharp },
                new ConversionSource { Content = NHibernateDocumentMapping, ContentType = ConversionContentType.XML },
            ]
            : [new ConversionSource { Content = JpaDocument, ContentType = ConversionContentType.Java }]);

    private static string EntityCode(ConversionResult result)
        => Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.CSharpEntity).Content;
    private const string VersionedSource = """
        public class Document
        {
            [Key]
            public int DocumentID { get; set; }

            [Timestamp]
            public byte[] RowVersion { get; set; }
        }
        """;

    [Fact]
    public void TimestampAnnotationSetsTheVersionFlag()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(VersionedSource);

        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "RowVersion");
        Assert.True(map.IsVersion);

        // The annotation used to fall into the unread-annotation branch; a mapped fact
        // must not be reported as dropped.
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Fact]
    public void EFCoreRoundTripKeepsTheAnnotation()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(VersionedSource);

        var code = builder.Build().Single().Content;
        Assert.Contains("[Timestamp]", code);

        // A byte array is the language type of a rowversion even where no family arrived,
        // so the store-generated reading stands and nothing is narrowed.
        Assert.DoesNotContain("[ConcurrencyCheck]", code);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);

        var reparsed = new EFCoreEntityBuilder();
        new EFCoreEntityParser(reparsed).Parse(code);
        Assert.True(reparsed.EntityMaps.Single().PropertyMaps
            .Single(pm => pm.Property.Name == "RowVersion").IsVersion);
    }

    [Fact]
    public void EFCoreLeavesTheStoreTypeOfABinaryVersionToTheAnnotation()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(VersionedSource);

        // The facts a catalog would supply for a rowversion column (decision 019).
        builder.SetPropertyDatabaseType("RowVersion", DatabaseType.VarBinary,
            sourceSqlType: "rowversion", length: 8);

        var code = builder.Build().Single().Content;

        // [Timestamp] itself makes the column a rowversion; a TypeName would override
        // that mapping with plain varbinary, and the length is the type's own.
        Assert.Contains("[Timestamp]", code);
        Assert.DoesNotContain("TypeName", code);
        Assert.DoesNotContain("MaxLength", code);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);
    }

    [Theory]
    [MemberData(nameof(FrameworkIncrementedSources))]
    public void EFCoreComparesANumericVersionAndReportsTheIncrementAsLost(ORMEnum source)
    {
        var result = ConvertNumericVersion(source, ORMEnum.EFCore);
        var code = EntityCode(result);

        // [Timestamp] would make EF Core expect the database to produce an int; the
        // comparison on write is what the annotations can keep.
        Assert.Contains("[ConcurrencyCheck]", code);
        Assert.DoesNotContain("[Timestamp]", code);

        var loss = Assert.Single(result.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("Revision", loss.Property);
        Assert.Equal(ORMEnum.EFCore, loss.Framework);
    }

    [Fact]
    public void EFCoreComparesADateTimeVersionAndReportsTheIncrementAsLost()
    {
        var builder = new EFCoreEntityBuilder();
        new NHibernateEntityParser(builder).Parse("""
            namespace Library;

            public class Document
            {
                public virtual int DocumentID { get; set; }

                public virtual DateTime Modified { get; set; }
            }
            """);
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Library">
                <class name="Document" table="Documents">
                    <id name="DocumentID" type="Int32">
                        <generator class="assigned" />
                    </id>
                    <timestamp name="Modified" />
                </class>
            </hibernate-mapping>
            """);

        var code = builder.Build().Single().Content;

        // NHibernate stamps the time itself on every write; EF Core would neither stamp it
        // nor send it under [Timestamp], so this is the same narrowing as the numeric one.
        Assert.Contains("[ConcurrencyCheck]", code);
        Assert.DoesNotContain("[Timestamp]", code);

        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("Modified", loss.Property);
    }

    [Fact]
    public void NHibernateWritesTheVersionElementBetweenIdAndProperties()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [Timestamp]
                public byte[] RowVersion { get; set; }

                public string? Title { get; set; }
            }
            """);
        builder.SetPropertyDatabaseType("RowVersion", DatabaseType.VarBinary,
            sourceSqlType: "rowversion", length: 8);

        var mapping = builder.Build().Single(o => o.ContentType == Model.ConversionContentType.XML).Content;

        // A binary version cannot be incremented by NHibernate itself, so the database
        // generates it; the literal type and the column facts ride the nested column.
        Assert.Contains("<version name=\"RowVersion\" generated=\"always\" type=\"binary\">", mapping);
        Assert.Contains("sql-type=\"rowversion\"", mapping);
        Assert.DoesNotContain("<property name=\"RowVersion\"", mapping);

        // The mapping schema places the element between the identifier and the properties.
        Assert.True(mapping.IndexOf("</id>") < mapping.IndexOf("<version"));
        Assert.True(mapping.IndexOf("</version>") < mapping.IndexOf("<property"));
    }

    [Fact]
    public void NHibernateVersionWithoutAStatedTypeClaimsOnlyTheFlag()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(VersionedSource);

        var mapping = builder.Build().Single(o => o.ContentType == Model.ConversionContentType.XML).Content;

        // No type family arrived, so neither type nor generated is claimed - NHibernate
        // infers the type from the persistent class, as with any property.
        Assert.Contains("<version name=\"RowVersion\">", mapping);
        Assert.DoesNotContain("generated=", mapping);
    }

    [Fact]
    public void NHibernateDropsASecondVersionFlagWithARecord()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [Timestamp]
                public byte[] RowVersion { get; set; }

                [Timestamp]
                public byte[] SecondVersion { get; set; }
            }
            """);

        var mapping = builder.Build().Single(o => o.ContentType == Model.ConversionContentType.XML).Content;

        // The schema admits a single <version> element; the second flag is a loss, and
        // the column itself survives as a plain property.
        Assert.Contains("<version name=\"RowVersion\">", mapping);
        Assert.Contains("<property name=\"SecondVersion\"", mapping);

        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("SecondVersion", loss.Property);
    }

    [Fact]
    public void DapperLosesTheVersionColumnWithARecord()
    {
        var builder = new DapperEntityBuilder();
        new EFCoreEntityParser(builder).Parse(VersionedSource);

        var outputs = builder.Build();

        // The artifact is generated - poorer, not refused - and the mechanical record
        // (decision 004) names the fact Dapper has nowhere to put.
        Assert.NotEmpty(outputs);
        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("RowVersion", loss.Property);
    }

    // --- The version the application keeps: EF Core's [ConcurrencyCheck] (decision 116) ---

    /// <summary>
    /// EF Core's own rendering of a numeric version: a concurrency token EF Core compares on
    /// write and nobody but the application produces.
    /// </summary>
    public const string ApplicationVersionedSource = """
        public class Document
        {
            [Key]
            public int DocumentID { get; set; }

            [ConcurrencyCheck]
            public int Revision { get; set; }

            public string? Title { get; set; }
        }
        """;

    [Fact]
    public void ConcurrencyCheckOnTheOneIntegralPropertyIsTheVersionTheApplicationKeeps()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(ApplicationVersionedSource);

        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Revision");
        Assert.True(map.IsVersion);
        Assert.True(map.IsApplicationManagedVersion);

        // The reading is exact - the model carries what the source said - so neither the
        // unread-annotation record nor any other record is due.
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);
    }

    [Fact]
    public void EFCoreRoundTripKeepsConcurrencyCheckWithoutARecord()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse(ApplicationVersionedSource);

        var code = builder.Build().Single().Content;

        // Nothing is narrowed: the application kept the version before and keeps it after.
        Assert.Contains("[ConcurrencyCheck]", code);
        Assert.DoesNotContain("[Timestamp]", code);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);

        var reparsed = new EFCoreEntityBuilder();
        new EFCoreEntityParser(reparsed).Parse(code);
        var map = reparsed.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Revision");
        Assert.True(map.IsVersion);
        Assert.True(map.IsApplicationManagedVersion);
    }

    [Theory]
    [InlineData("int?")]
    [InlineData("long")]
    [InlineData("short")]
    [InlineData("DateTime")]
    public void EveryTypeTheTargetsLeadAVersionOverIsRead(string type)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse($$"""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [ConcurrencyCheck]
                public {{type}} Revision { get; set; }
            }
            """);

        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Revision");
        Assert.True(map.IsVersion);
        Assert.True(map.IsApplicationManagedVersion);
        Assert.DoesNotContain(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("Guid")]
    [InlineData("decimal")]
    [InlineData("byte[]")]
    public void ConcurrencyCheckOnAnotherTypeProtectsTheColumnAndIsNotAVersion(string type)
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse($$"""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [ConcurrencyCheck]
                public {{type}} Token { get; set; }
            }
            """);

        // No target leads a version over such a type; the token stays what EF Core makes
        // of it - a guarded column - and the record names the condition that failed
        // rather than claiming the model has no place for the annotation.
        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "Token");
        Assert.False(map.IsVersion);
        Assert.False(map.IsApplicationManagedVersion);

        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("Token", loss.Property);
        Assert.Contains(type, loss.Reason);
    }

    [Fact]
    public void TwoConcurrencyTokensAreNeitherOfThemTheVersion()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [ConcurrencyCheck]
                public int Revision { get; set; }

                [ConcurrencyCheck]
                public int Generation { get; set; }
            }
            """);

        // Two tokens guard two columns; a version is one per entity.
        Assert.DoesNotContain(builder.EntityMaps.Single().PropertyMaps, pm => pm.IsVersion);

        var losses = builder.Records
            .Where(r => r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn)
            .Select(r => r.Property)
            .ToList();
        Assert.Equal(["Revision", "Generation"], losses);
    }

    [Fact]
    public void ConcurrencyCheckBesideATimestampIsNotASecondVersion()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [Timestamp]
                public byte[] RowVersion { get; set; }

                [ConcurrencyCheck]
                public int Revision { get; set; }
            }
            """);

        var maps = builder.EntityMaps.Single().PropertyMaps;
        Assert.True(maps.Single(pm => pm.Property.Name == "RowVersion").IsVersion);
        Assert.False(maps.Single(pm => pm.Property.Name == "Revision").IsVersion);

        var loss = Assert.Single(builder.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal("Revision", loss.Property);
    }

    [Fact]
    public void ConcurrencyCheckOnTheTimestampPropertyStatesNothingNew()
    {
        var builder = new EFCoreEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Document
            {
                [Key]
                public int DocumentID { get; set; }

                [Timestamp]
                [ConcurrencyCheck]
                public byte[] RowVersion { get; set; }
            }
            """);

        var map = builder.EntityMaps.Single().PropertyMaps.Single(pm => pm.Property.Name == "RowVersion");
        Assert.True(map.IsVersion);
        Assert.False(map.IsApplicationManagedVersion);
        Assert.DoesNotContain(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
    }

    [Fact]
    public void NHibernateTakesOverTheIncrementOfAnApplicationVersionWithARecord()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse(ApplicationVersionedSource);

        var mapping = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // The version element is NHibernate's; it increments it itself, which the source
        // left to the application - a convention of the target, not a loss.
        Assert.Contains("<version name=\"Revision\"", mapping);
        Assert.DoesNotContain("generated=", mapping);

        var record = Assert.Single(builder.Records, r => r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal(ConversionRecordKind.Convention, record.Kind);
        Assert.Equal("Revision", record.Property);
    }

    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void JpaTakesOverTheIncrementOfAnApplicationVersionWithARecord(ORMEnum target)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target,
            [new ConversionSource { Content = ApplicationVersionedSource, ContentType = ConversionContentType.CSharp }]);

        var code = Assert.Single(result.Sources, s => s.ContentType == ConversionContentType.JavaEntity).Content;
        Assert.Contains("@Version", code);

        var record = Assert.Single(result.Records, r => r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal(ConversionRecordKind.Convention, record.Kind);
        Assert.Equal(target, record.Framework);
        Assert.Equal("Revision", record.Property);
    }

    [Fact]
    public void AFrameworkIncrementedVersionIsStillNarrowedIntoEFCore()
    {
        // The other half of decision 116: NHibernate's version over Int32 is not the
        // application's, so EF Core still records the increment it cannot state.
        var result = ConvertNumericVersion(ORMEnum.NHibernate, ORMEnum.EFCore);

        var loss = Assert.Single(result.Records, r => r.Category == MappingFactCategory.VersionColumn);
        Assert.Equal(ConversionRecordKind.Loss, loss.Kind);
    }
}
