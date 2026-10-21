using System.Text.Json;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Decision 029: a database connection is the consumer project's fact, so no builder writes
/// one. The S4 ban on credentials binds every generated source and every text about a run
/// (decision 160); this class holds the translation's output, the harness and the log have
/// tests of their own. Without it the artifact half held by construction only, so a builder
/// that started emitting a configuration file (hibernate.cfg.xml, persistence.xml) would
/// break it without a test noticing.
/// Directions and inputs come from <see cref="CrossFrameworkInputs"/>.
/// </summary>
public class ArtifactCarriesNoCredentialsTest
{
    /// <summary>
    /// What a connection betrays itself by in any of the emitted languages: the
    /// connection-string keys themselves, the fluent registration, and the configuration
    /// files of both ecosystems that would carry one. Matched case-insensitively.
    /// </summary>
    private static readonly string[] ConnectionMarkers =
    [
        "connectionstring",
        "connection_string",
        "data source=",
        "server=",
        "user id=",
        "password",
        "integrated security",
        "usesqlserver",
        "hibernate.cfg",
        "persistence.xml",
    ];

    private static void AssertConnectionFree(ConversionResult result)
    {
        Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, artifact =>
        {
            foreach (var marker in ConnectionMarkers)
            {
                Assert.DoesNotContain(marker, artifact.Content, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void NoDirectionWritesAConnectionIntoItsArtifacts(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        AssertConnectionFree(result);
    }

    /// <summary>
    /// The stronger half of decision 029: connection code in the input is application code,
    /// not a mapping fact, so parsers do not read it and no target writes it back. The
    /// embedded DbContext is read as the framework's API, not as an entity (decisions 111
    /// and 132), and whichever reading takes it, the secret must not survive into any artifact -
    /// nor, since decision 160 reads a record as a log, into a record or the ledger, which is
    /// why the whole result is searched rather than its artifacts.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Frameworks), MemberType = typeof(CrossFrameworkInputs))]
    public void AConnectionStringInTheInputNeverReachesTheOutput(ORMEnum target)
    {
        const string contextWithConnection = """
            using Microsoft.EntityFrameworkCore;

            namespace Shop;

            public class ShopContext : DbContext
            {
                protected override void OnConfiguring(DbContextOptionsBuilder options)
                    => options.UseSqlServer("Server=db;Database=Shop;User Id=sa;Password=TopSecret1!");
            }
            """;

        List<ConversionSource> sources =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.EFCore),
            new() { Content = contextWithConnection, ContentType = ConversionContentType.CSharp },
            CrossFrameworkInputs.QueryUnit(ORMEnum.EFCore),
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, sources);

        Assert.DoesNotContain("TopSecret1", JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        AssertConnectionFree(result);
    }
}
