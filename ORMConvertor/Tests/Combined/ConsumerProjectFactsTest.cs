using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Decision 040: a fact about the project that will compile and run the output is not ours
/// to invent. Decisions 028 and 029 said it once for the assembly name and once for the
/// database connection; this test holds the rule itself over every direction the enum
/// yields (<see cref="CrossFrameworkInputs"/>), so the next consumer-project fact cannot
/// enter an artifact unnoticed - which is how the assembly name got there in the first place.
/// </summary>
public class ConsumerProjectFactsTest
{
    /// <summary>
    /// What a consumer-project fact looks like once it reaches an artifact: the project file
    /// and the dependencies it declares, the registration that belongs to the consumer's
    /// startup, and the configuration files of both ecosystems. Credentials are the same rule
    /// seen from the S4 side and belong to <see cref="ArtifactCarriesNoCredentialsTest"/>.
    /// Matched case-insensitively.
    /// </summary>
    private static readonly string[] ConsumerProjectMarkers =
    [
        "<project sdk",
        "<packagereference",
        "assembly=",
        "adddbcontext",
        "hibernate.cfg",
        "persistence.xml",
        "appsettings",
    ];

    /// <summary>
    /// Namespaces an artifact declares, in any language it can declare one in: the C#
    /// declaration, the root of an NHibernate mapping and the Java package. Query artifacts
    /// declare none, which is why the assertions below read the entity and mapping artifacts.
    ///
    /// The namespace attribute of a MyBatis mapper is the odd one out: it names the mapper
    /// itself - package and type together, the way MyBatis addresses a statement - so the
    /// package is its leading part and the last segment is the artifact's own name
    /// (decision 084). Read whole it would look like a namespace the tool invented.
    /// </summary>
    private static List<string> DeclaredNamespaces(ConversionSource artifact)
    {
        var isMyBatisMapper = artifact.Content.Contains("<mapper ", StringComparison.Ordinal);

        return
        [
            .. Regex.Matches(artifact.Content, @"namespace\s+([\w.]+)\s*[;{]").Select(m => m.Groups[1].Value),
            .. Regex.Matches(artifact.Content, "namespace=\"([^\"]+)\"")
                .Select(m => isMyBatisMapper ? PackageOf(m.Groups[1].Value) : m.Groups[1].Value)
                .Where(n => n.Length > 0),
            .. Regex.Matches(artifact.Content, @"package\s+([\w.]+)\s*;").Select(m => m.Groups[1].Value),
        ];
    }

    private static string PackageOf(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        return lastDot < 0 ? string.Empty : qualifiedName[..lastDot];
    }

    private static List<ConversionSource> MappingArtifacts(ConversionResult result) =>
    [
        .. result.Sources.Where(a => a.ContentType
            is ConversionContentType.CSharpEntity or ConversionContentType.XML or ConversionContentType.JavaEntity)
    ];

    /// <summary>
    /// A Dapper source states no primary key, so a target that requires one refuses the
    /// entity at the completeness gate of decision 010 and says so in a failure record;
    /// without a catalog connection there is nothing to complete it from (decision 015).
    /// Such a direction hands over the query alone and has no namespace to carry - the
    /// assertions below therefore check that the refusal was spoken, not that an artifact
    /// exists.
    /// </summary>
    private static bool RefusedTheEntity(ConversionResult result, List<ConversionSource> artifacts)
    {
        if (artifacts.Count > 0)
        {
            return false;
        }

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        return true;
    }

    /// <summary>
    /// The namespace is a fact of the source code, not of the consumer project - the same
    /// class carries it into whatever project compiles it - so it travels through the
    /// conversion unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void EveryDirectionCarriesTheSourceNamespaceUnchanged(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source, withNamespace: true));

        var artifacts = MappingArtifacts(result);
        if (RefusedTheEntity(result, artifacts))
        {
            return;
        }

        Assert.Contains(artifacts, a => DeclaredNamespaces(a).Contains(CrossFrameworkInputs.Namespace));
        Assert.All(artifacts, a => Assert.All(DeclaredNamespaces(a), n => Assert.Equal(CrossFrameworkInputs.Namespace, n)));
    }

    /// <summary>
    /// And a namespace the source does not have is not filled in from anywhere - not from a
    /// default of the target framework and not from the entity name. An artifact in the global
    /// namespace is what the input said.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void NoDirectionInventsANamespaceTheSourceDoesNotHave(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source, withNamespace: false));

        var artifacts = MappingArtifacts(result);
        if (RefusedTheEntity(result, artifacts))
        {
            return;
        }

        Assert.All(artifacts, a => Assert.Empty(DeclaredNamespaces(a)));
    }

    /// <summary>
    /// No direction hands over an artifact of the consumer project, and none of the artifacts
    /// it does hand over states one of its facts. The check runs over every artifact, queries
    /// included, because a builder that started emitting a project file or a configuration
    /// would do it beside the entity, not inside it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void NoDirectionStatesAFactOfTheConsumerProject(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source, withNamespace: true));

        Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, artifact =>
        {
            foreach (var marker in ConsumerProjectMarkers)
            {
                Assert.DoesNotContain(marker, artifact.Content, StringComparison.OrdinalIgnoreCase);
            }
        });
    }
}
