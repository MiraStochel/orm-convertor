using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Model;
using OrmConvertor;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// The EF Core builder over an entity without a key (decision 177): a navigation EF Core cannot
/// map without the key of the relationship's principal is written [NotMapped], with an
/// incompleteness where no relation stands behind it and a loss of the relation where one
/// does, and the model EF Core builds from the artifact is accepted - where it used to refuse
/// the whole model and with it every query of the conversion. What EF Core does with each
/// shape is measured in <see cref="EFCoreKeylessNavigationProviderTest"/>.
/// </summary>
public class EFCoreKeylessNavigationTest
{
    private static ConversionSource Source(string content, string name) => new()
    {
        Name = name,
        ContentType = ConversionContentType.CSharp,
        Content = content,
    };

    private static byte[] Compile(ConversionResult result, string name)
        => GeneratedEntityCompiler.CompileOrFail(
            "EFCoreKeylessNavigation_" + name,
            result.Sources.Where(s => s.ContentType == ConversionContentType.CSharpEntity).Select(s => s.Content),
            GeneratedEntityCompiler.EFCoreConsumerReferences);

    /// <summary>Chinook's shape from a source that states no key: Dapper without a catalog.</summary>
    private const string Chinook = """
        namespace Music;

        public class Artist
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public virtual ICollection<Album> Albums { get; set; }
        }

        public class Album
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public int ArtistId { get; set; }
            public virtual Artist Artist { get; set; }
        }
        """;

    [Fact]
    public void NavigationsBetweenEntitiesWithoutAKeyAreNotMappedAndTheModelBuilds()
    {
        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, [Source(Chinook, "Music.cs")]);

        var album = Assert.Single(result.Sources, s => s.Entity == "Album").Content.Replace("\r\n", "\n");
        var artist = Assert.Single(result.Sources, s => s.Entity == "Artist").Content.Replace("\r\n", "\n");
        Assert.Contains("[NotMapped]\n    public virtual required Artist Artist", album, StringComparison.Ordinal);
        Assert.Contains("public required int ArtistId", album, StringComparison.Ordinal);
        Assert.Contains("[NotMapped]\n    public virtual required List<Album> Albums", artist, StringComparison.Ordinal);

        foreach (var (entity, property, keyless) in new[] { ("Album", "Artist", "Artist"), ("Artist", "Albums", "Album") })
        {
            var record = Assert.Single(result.Records, r => r.Entity == entity && r.Property == property);
            Assert.Equal(ConversionRecordKind.Incompleteness, record.Kind);
            Assert.Null(record.Category);
            Assert.StartsWith($"'{keyless}' has no key", record.Reason, StringComparison.Ordinal);
        }

        var model = EFCoreAcceptance.BuildModel(Compile(result, "Chinook"));
        Assert.Empty(model.FindEntityType("Music.Album")!.GetNavigations());
        Assert.NotNull(model.FindEntityType("Music.Album")!.FindProperty("ArtistId"));
    }

    [Fact]
    public void ARelationOverAnEntityWithoutAKeyIsALossOfItsForeignKeyColumns()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore,
        [
            Source("""
                using System.ComponentModel.DataAnnotations.Schema;
                using Microsoft.EntityFrameworkCore;

                namespace Music;

                [Keyless]
                public class Artist
                {
                    public int Id { get; set; }
                }

                [Keyless]
                public class Album
                {
                    public int ArtistId { get; set; }

                    [ForeignKey("ArtistId")]
                    public Artist Artist { get; set; }
                }
                """, "Music.cs"),
        ]);

        var album = Assert.Single(result.Sources, s => s.Entity == "Album").Content;
        Assert.Contains("[NotMapped]", album, StringComparison.Ordinal);
        Assert.DoesNotContain("[ForeignKey", album, StringComparison.Ordinal);

        // Beside the resolution phase's record that the columns found no key to pair with.
        var loss = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Loss && r.Entity == "Album" && r.Property == "Artist");
        Assert.Equal(MappingFactCategory.ForeignKeyColumns, loss.Category);
        Assert.StartsWith("'Artist' has no key", loss.Reason, StringComparison.Ordinal);

        // The ledger never counts the relation as carried - its columns pair with no key, so it
        // has no row at all - and the experiment's reference and its read-back agree about it
        // (decisions 124 and 173).
        Assert.DoesNotContain(result.Facts, f => f.Entity == "Album" && f.Property == "Artist"
            && f.Category == MappingFactCategory.ForeignKeyColumns && f.Outcome == FactOutcome.Carried);

        EFCoreAcceptance.BuildModel(Compile(result, "Relation"));
    }

    [Fact]
    public void AReferenceFromAnEntityWithoutAKeyToOneWithAKeyStaysMapped()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.EFCore,
        [
            Source("""
                using System.ComponentModel.DataAnnotations;
                using System.ComponentModel.DataAnnotations.Schema;
                using Microsoft.EntityFrameworkCore;

                namespace Music;

                public class Artist
                {
                    [Key]
                    public int Id { get; set; }
                }

                [Keyless]
                public class Album
                {
                    public int ArtistId { get; set; }

                    [ForeignKey("ArtistId")]
                    public Artist Artist { get; set; }
                }
                """, "Music.cs"),
        ]);

        var album = Assert.Single(result.Sources, s => s.Entity == "Album").Content;
        Assert.DoesNotContain("[NotMapped]", album, StringComparison.Ordinal);
        Assert.Contains("[ForeignKey(\"ArtistId\")]", album, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("decision 177", StringComparison.Ordinal) || r.Reason.Contains("decisions 015 and 177", StringComparison.Ordinal));

        var model = EFCoreAcceptance.BuildModel(Compile(result, "KeyedTarget"));
        Assert.NotNull(model.FindEntityType("Music.Album")!.FindNavigation("Artist"));
    }
}
