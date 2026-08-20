using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;

namespace Tests.Combined;

/// <summary>
/// The version column as a mapping fact of its own (decision 030): a flag in the model
/// rather than a database type, expressed as [Timestamp] by EF Core and as the version
/// element by NHibernate, and inexpressible in Dapper, where the mechanical loss record
/// states a property of Dapper rather than of the tool.
/// </summary>
public class VersionColumnTest
{
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
}
