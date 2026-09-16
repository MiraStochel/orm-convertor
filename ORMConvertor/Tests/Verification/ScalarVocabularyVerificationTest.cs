using AbstractWrappers;
using DapperWrappers;
using EFCoreWrappers;
using Microsoft.EntityFrameworkCore;
using Model;
using NHibernateWrappers;

namespace Tests.Verification;

/// <summary>
/// Second and third verification levels of decision 016 for the five scalars of decision
/// 071. What matters here is acceptance, not shape: the generated classes compile, EF
/// Core's SQL Server provider builds a model over them and picks the expected store
/// types, and NHibernate binds every emitted type name - the registered ones for
/// DateOnly and TimeOnly included - to the compiled class in a session factory. The run
/// is dry; no database takes part.
/// </summary>
public class ScalarVocabularyVerificationTest
{
    /// <summary>The five types with nothing stated about their columns: each target applies its own default.</summary>
    private const string EventSource = """
        namespace ScalarEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Events")]
        public class Event
        {
            [Key]
            public int EventId { get; set; }

            public DateOnly Day { get; set; }

            public TimeOnly? StartsAt { get; set; }

            public DateTimeOffset RecordedAt { get; set; }

            public TimeSpan Elapsed { get; set; }

            public byte[] Payload { get; set; } = [];
        }
        """;

    /// <summary>
    /// The same five with their columns stated, so that the NHibernate mapping has to name
    /// a type for each pair - DateOnlyAsDate, TimeOnlyAsTime, DateTimeOffset, TimeAsTimeSpan
    /// for the time column EF Core would give a TimeSpan, and binary.
    /// </summary>
    private const string StatedEventSource = """
        namespace ScalarEntities;

        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        [Table("Events")]
        public class Event
        {
            [Key]
            public int EventId { get; set; }

            [Column(TypeName = "date")]
            public DateOnly Day { get; set; }

            [Column(TypeName = "time")]
            public TimeOnly? StartsAt { get; set; }

            [Column(TypeName = "datetimeoffset")]
            public DateTimeOffset RecordedAt { get; set; }

            [Column(TypeName = "time")]
            public TimeSpan Elapsed { get; set; }

            [Column(TypeName = "varbinary(max)")]
            public byte[] Payload { get; set; } = [];
        }
        """;

    private static List<ConversionSource> Convert(AbstractEntityBuilder builder, string source)
    {
        new EFCoreEntityParser(builder).Parse(source);
        return builder.Build();
    }

    private static byte[] CompileEntities(IEnumerable<ConversionSource> outputs, IReadOnlyList<Microsoft.CodeAnalysis.MetadataReference> references)
        => GeneratedEntityCompiler.CompileOrFail(
            "ScalarEntities",
            outputs.Where(o => o.ContentType == ConversionContentType.CSharpEntity).Select(o => o.Content),
            references);

    [Fact]
    public void EFCoreBuildsAModelAndPicksItsStoreTypes()
    {
        var outputs = Convert(new EFCoreEntityBuilder(), EventSource);
        var model = EFCoreAcceptance.BuildModel(CompileEntities(outputs, GeneratedEntityCompiler.EFCoreConsumerReferences));

        var entity = model.GetEntityTypes().Single();
        string ColumnType(string name) => entity.GetProperty(name).GetColumnType();

        // The provider's own defaults for the five CLR types, verified at runtime before
        // the decision was written (decision 071).
        Assert.Equal("date", ColumnType("Day"));
        Assert.Equal("time", ColumnType("StartsAt"));
        Assert.Equal("datetimeoffset", ColumnType("RecordedAt"));
        Assert.Equal("time", ColumnType("Elapsed"));
        Assert.Equal("varbinary(max)", ColumnType("Payload"));
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryWhenItInfersTheTypesItself()
    {
        var outputs = Convert(new NHibernateEntityBuilder(), EventSource);

        // No column is stated, so no property carries a type attribute; NHibernate binds
        // the five CLR types by its own heuristic. Completing without an exception is
        // the verdict.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            outputs.Where(o => o.ContentType == ConversionContentType.XML).Select(o => o.Content));
    }

    [Fact]
    public void NHibernateBuildsASessionFactoryOverEveryEmittedTypeName()
    {
        var outputs = Convert(new NHibernateEntityBuilder(), StatedEventSource);
        var mapping = outputs.Single(o => o.ContentType == ConversionContentType.XML).Content;

        // The names the pair table chose are in the mapping ...
        Assert.Contains("type=\"DateOnlyAsDate\"", mapping);
        Assert.Contains("type=\"TimeOnlyAsTime\"", mapping);
        Assert.Contains("type=\"DateTimeOffset\"", mapping);
        Assert.Contains("type=\"TimeAsTimeSpan\"", mapping);
        Assert.Contains("type=\"binary\"", mapping);

        // ... and every one of them resolves in TypeFactory of 5.7.0 and reads the
        // compiled property: the session factory build is what proves it.
        NHibernateAcceptance.BuildSessionFactory(
            CompileEntities(outputs, GeneratedEntityCompiler.NHibernateConsumerReferences),
            [mapping]);
    }

    [Fact]
    public void DapperClassCompilesUnderTheCSharpNames()
    {
        CompileEntities(Convert(new DapperEntityBuilder(), EventSource), GeneratedEntityCompiler.EFCoreConsumerReferences);
    }
}
