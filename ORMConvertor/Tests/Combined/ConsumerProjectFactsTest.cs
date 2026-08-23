using System.Text.RegularExpressions;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Decision 040: a fact about the project that will compile and run the output is not ours
/// to invent. Decisions 028 and 029 said it once for the assembly name and once for the
/// database connection; this test holds the rule itself over all nine directions, so the
/// next consumer-project fact cannot enter an artifact unnoticed - which is how the assembly
/// name got there in the first place.
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

    private const string SourceNamespace = "Shop";

    private static string NamespaceLine(bool withNamespace) =>
        withNamespace ? $"namespace {SourceNamespace};{Environment.NewLine}{Environment.NewLine}" : string.Empty;

    private static string DapperEntity(bool withNamespace) =>
        NamespaceLine(withNamespace) + """
        public class Customer
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private static string EFCoreEntity(bool withNamespace) =>
        """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;


        """ + NamespaceLine(withNamespace) + """
        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private static string NHibernateEntity(bool withNamespace) =>
        NamespaceLine(withNamespace) + """
        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }
        """;

    private static string NHibernateMapping(bool withNamespace)
    {
        var namespaceAttribute = withNamespace ? $" namespace=\"{SourceNamespace}\"" : string.Empty;

        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2"{namespaceAttribute}>
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerName" type="String" />
            <property name="CreditLimit" column="CreditLimit" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;
    }

    private const string DapperQuery = """
        SELECT c.CustomerName
        FROM Sales.Customers AS c
        WHERE c.CreditLimit > 2000
        ORDER BY c.CustomerName ASC
        """;

    private const string EFCoreQuery = """
        public void Query()
        {
            var q = ctx.Customers
                .Where(c => c.CreditLimit > 2000)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;

    private const string NHibernateQuery = """
        public void Query()
        {
            var q = session.Query<Customer>()
                .Where(c => c.CreditLimit > 2000)
                .OrderBy(c => c.CustomerName)
                .Select(c => new { Name = c.CustomerName })
                .ToList();
        }
        """;

    private static List<ConversionSource> SourcesFor(ORMEnum source, bool withNamespace) => source switch
    {
        ORMEnum.Dapper =>
        [
            new() { Content = DapperEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
            new() { Content = DapperQuery, ContentType = ConversionContentType.SqlQuery },
        ],
        ORMEnum.EFCore =>
        [
            new() { Content = EFCoreEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
            new() { Content = EFCoreQuery, ContentType = ConversionContentType.CSharpQuery },
        ],
        _ =>
        [
            new() { Content = NHibernateEntity(withNamespace), ContentType = ConversionContentType.CSharpEntity },
            new() { Content = NHibernateMapping(withNamespace), ContentType = ConversionContentType.XML },
            new() { Content = NHibernateQuery, ContentType = ConversionContentType.CSharpQuery },
        ],
    };

    /// <summary>
    /// Namespaces an artifact declares, in either language it can declare one in: the C#
    /// declaration and the root of an NHibernate mapping. Query artifacts declare none, which
    /// is why the assertions below read the entity and mapping artifacts.
    /// </summary>
    private static List<string> DeclaredNamespaces(ConversionSource artifact) =>
    [
        .. Regex.Matches(artifact.Content, @"namespace\s+([\w.]+)\s*[;{]").Select(m => m.Groups[1].Value),
        .. Regex.Matches(artifact.Content, "namespace=\"([^\"]+)\"").Select(m => m.Groups[1].Value),
    ];

    private static List<ConversionSource> MappingArtifacts(ConversionResult result) =>
    [
        .. result.Sources.Where(a => a.ContentType
            is ConversionContentType.CSharpEntity or ConversionContentType.XML)
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

    public static TheoryData<ORMEnum, ORMEnum> Directions()
    {
        var data = new TheoryData<ORMEnum, ORMEnum>();
        foreach (var source in new[] { ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate })
        {
            foreach (var target in new[] { ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate })
            {
                data.Add(source, target);
            }
        }

        return data;
    }

    /// <summary>
    /// The namespace is a fact of the source code, not of the consumer project - the same
    /// class carries it into whatever project compiles it - so it travels through the
    /// conversion unchanged.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void EveryDirectionCarriesTheSourceNamespaceUnchanged(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source, withNamespace: true));

        var artifacts = MappingArtifacts(result);
        if (RefusedTheEntity(result, artifacts))
        {
            return;
        }

        Assert.Contains(artifacts, a => DeclaredNamespaces(a).Contains(SourceNamespace));
        Assert.All(artifacts, a => Assert.All(DeclaredNamespaces(a), n => Assert.Equal(SourceNamespace, n)));
    }

    /// <summary>
    /// And a namespace the source does not have is not filled in from anywhere - not from a
    /// default of the target framework and not from the entity name. An artifact in the global
    /// namespace is what the input said.
    /// </summary>
    [Theory]
    [MemberData(nameof(Directions))]
    public void NoDirectionInventsANamespaceTheSourceDoesNotHave(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source, withNamespace: false));

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
    [MemberData(nameof(Directions))]
    public void NoDirectionStatesAFactOfTheConsumerProject(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source, withNamespace: true));

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
