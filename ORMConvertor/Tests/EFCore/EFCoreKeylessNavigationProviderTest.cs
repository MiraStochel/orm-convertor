using Microsoft.EntityFrameworkCore.Metadata;
using Tests.Verification;

namespace Tests.EFCore;

/// <summary>
/// Which navigations EF Core 10 maps between entities of which a side has no key, measured by
/// its own model validation without a connection (the third level of decision 016) - the fact
/// the EF Core builder's unmapped navigation rests on (decision 177). A relationship needs a
/// principal key: a reference a keyless entity holds as the dependent, to a keyed one, is
/// mapped; a navigation to a keyless entity refuses the whole model; a collection on a keyless
/// entity is ignored and an inverse reference on one gets a foreign key column invented on the
/// keyless side. [NotMapped] leaves the model buildable with the foreign key column still a
/// property, and a join over the key columns reads keyless entities.
/// </summary>
public class EFCoreKeylessNavigationProviderTest
{
    private static string Entities(bool artistKeyed, bool albumKeyed, string albumNavigation, string artistNavigation) => $$"""
        using System.Collections.Generic;
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;
        using Microsoft.EntityFrameworkCore;

        namespace Keyless;

        {{(artistKeyed ? string.Empty : "[Keyless]")}}
        [Table("Artist")]
        public class Artist
        {
            {{(artistKeyed ? "[Key]" : string.Empty)}}
            public int Id { get; set; }
            public string? Name { get; set; }
            {{artistNavigation}}
        }

        {{(albumKeyed ? string.Empty : "[Keyless]")}}
        [Table("Album")]
        public class Album
        {
            {{(albumKeyed ? "[Key]" : string.Empty)}}
            public int Id { get; set; }
            public string Title { get; set; } = "";
            public int ArtistId { get; set; }
            {{albumNavigation}}
        }
        """;

    private const string Reference = "public virtual Artist Artist { get; set; } = null!;";
    private const string Collection = "public virtual List<Album> Albums { get; set; } = [];";

    private static IModel Build(string name, string entities)
        => EFCoreAcceptance.BuildModel(GeneratedEntityCompiler.CompileOrFail(
            "EFCoreKeylessNavigation_" + name,
            [entities],
            GeneratedEntityCompiler.EFCoreConsumerReferences));

    private static Exception Refused(string name, string entities)
        => Assert.ThrowsAny<InvalidOperationException>(() => Build(name, entities));

    [Fact]
    public void AReferenceBetweenTwoKeylessEntitiesRefusesTheModel()
    {
        var refused = Refused("KeylessToKeyless", Entities(artistKeyed: false, albumKeyed: false, Reference, string.Empty));

        Assert.Contains("Unable to determine the relationship represented by navigation 'Album.Artist'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceFromAKeylessEntityToAKeyedOneIsMapped()
    {
        var model = Build("KeylessToKeyed", Entities(artistKeyed: true, albumKeyed: false, Reference, string.Empty));

        var album = model.FindEntityType("Keyless.Album")!;
        Assert.Null(album.FindPrimaryKey());
        Assert.NotNull(album.FindNavigation("Artist"));
        Assert.Equal(["ArtistId"], album.GetForeignKeys().Single().Properties.Select(p => p.Name));
    }

    [Fact]
    public void ACollectionOnAKeylessEntityIsIgnoredWithoutAWord()
    {
        // The keyless entity would be the principal, so EF Core maps neither the navigation
        // nor the foreign key behind it - and says nothing.
        var model = Build("KeylessCollection", Entities(artistKeyed: false, albumKeyed: true, string.Empty, Collection));

        Assert.Empty(model.FindEntityType("Keyless.Artist")!.GetNavigations());
        Assert.Empty(model.FindEntityType("Keyless.Album")!.GetForeignKeys());
    }

    [Fact]
    public void AnInverseReferenceOnAKeylessEntityInventsAForeignKeyColumnOnIt()
    {
        // The foreign key is Profile.CustomerId; a keyless entity cannot be a principal, so
        // EF Core turns the reference around and puts a shadow column on the keyless side.
        var model = Build("KeylessInverse", """
            using System.ComponentModel.DataAnnotations;
            using Microsoft.EntityFrameworkCore;

            namespace Keyless;

            [Keyless]
            public class Customer
            {
                public int Id { get; set; }
                public virtual Profile Profile { get; set; } = null!;
            }

            public class Profile
            {
                [Key]
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }
            """);

        var foreignKey = model.FindEntityType("Keyless.Customer")!.GetForeignKeys().Single();
        Assert.Equal(["ProfileId"], foreignKey.Properties.Select(p => p.Name));
        Assert.True(foreignKey.Properties.Single().IsShadowProperty());
    }

    [Fact]
    public void AReferenceFromAKeyedEntityToAKeylessOneRefusesTheModel()
    {
        // The reference sits on Album, keyed, and points at Artist, keyless.
        Refused("KeyedToKeyless", Entities(artistKeyed: false, albumKeyed: true, Reference, string.Empty));
    }

    [Fact]
    public void ACollectionOfKeylessEntitiesRefusesTheModel()
    {
        Refused("CollectionOfKeyless", Entities(artistKeyed: true, albumKeyed: false, string.Empty, Collection));
    }

    [Fact]
    public void NotMappedNavigationsLeaveTheModelBuildableWithTheForeignKeyColumn()
    {
        var model = Build("NotMapped", Entities(
            artistKeyed: false,
            albumKeyed: false,
            "[NotMapped] " + Reference,
            "[NotMapped] " + Collection));

        var album = model.FindEntityType("Keyless.Album")!;
        Assert.Empty(album.GetNavigations());
        Assert.NotNull(album.FindProperty("ArtistId"));
        Assert.Empty(model.FindEntityType("Keyless.Artist")!.GetNavigations());
    }

    [Fact]
    public void AJoinOverKeyColumnsReadsTwoKeylessEntities()
    {
        var method = """
            public static IQueryable Query(DbContext ctx)
            {
                return ctx.Set<Album>().Join(ctx.Set<Artist>(), a => a.ArtistId, r => r.Id, (a, r) => new { a.Title, r.Name });
            }
            """;

        var compiled = GeneratedQueryCompiler.CompileOrFail(
            "EFCoreKeylessNavigation_Join",
            method,
            [Entities(artistKeyed: false, albumKeyed: false, "[NotMapped] " + Reference, "[NotMapped] " + Collection)],
            GeneratedQueryCompiler.EFCoreConsumerReferences,
            "using Microsoft.EntityFrameworkCore; using Keyless;");

        var sql = EFCoreQueryAcceptance.Translate(compiled).Replace("\r\n", " ");

        Assert.Contains("INNER JOIN [Artist]", sql, StringComparison.Ordinal);
    }
}
