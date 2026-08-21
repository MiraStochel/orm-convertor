using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Decision 029: a database connection is the consumer project's fact, and the S4 ban on
/// credentials binds the handed-over artifact. Until now that held by construction only -
/// no builder writes a connection - so a builder that started emitting a configuration
/// file (hibernate.cfg.xml, persistence.xml) would break it without a test noticing.
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

    private const string DapperEntity = """
        namespace Shop;

        public class Customer
        {
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private const string EFCoreEntity = """
        using System.ComponentModel.DataAnnotations;
        using System.ComponentModel.DataAnnotations.Schema;

        namespace Shop;

        [Table("Customers", Schema = "Sales")]
        public class Customer
        {
            [Key]
            public int CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal CreditLimit { get; set; }
        }
        """;

    private const string NHibernateEntity = """
        namespace Shop;

        public class Customer
        {
            public virtual int CustomerId { get; set; }
            public virtual string CustomerName { get; set; }
            public virtual decimal CreditLimit { get; set; }
        }
        """;

    private const string NHibernateMapping = """
        <?xml version="1.0" encoding="utf-8"?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2" namespace="Shop" assembly="Shop">
          <class name="Customer" table="Customers" schema="Sales">
            <id name="CustomerId" column="CustomerId" type="Int32">
              <generator class="identity" />
            </id>
            <property name="CustomerName" column="CustomerName" type="String" />
            <property name="CreditLimit" column="CreditLimit" type="Decimal" />
          </class>
        </hibernate-mapping>
        """;

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

    private static List<ConversionSource> SourcesFor(ORMEnum source) => source switch
    {
        ORMEnum.Dapper =>
        [
            new() { Content = DapperEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = DapperQuery, ContentType = ConversionContentType.SqlQuery },
        ],
        ORMEnum.EFCore =>
        [
            new() { Content = EFCoreEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = EFCoreQuery, ContentType = ConversionContentType.CSharpQuery },
        ],
        _ =>
        [
            new() { Content = NHibernateEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = NHibernateMapping, ContentType = ConversionContentType.XML },
            new() { Content = NHibernateQuery, ContentType = ConversionContentType.CSharpQuery },
        ],
    };

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

    [Theory]
    [MemberData(nameof(Directions))]
    public void NoDirectionWritesAConnectionIntoItsArtifacts(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source));

        AssertConnectionFree(result);
    }

    /// <summary>
    /// The stronger half of decision 029: connection code in the input is application code,
    /// not a mapping fact, so parsers do not read it and no target writes it back. The
    /// embedded DbContext still passes the entity parser as another entity - a known F14
    /// gap - but even then the secret must not survive into any artifact.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.EFCore)]
    [InlineData(ORMEnum.NHibernate)]
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
            new() { Content = EFCoreEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = contextWithConnection, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = EFCoreQuery, ContentType = ConversionContentType.CSharpQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, sources);

        Assert.All(result.Sources, artifact =>
            Assert.DoesNotContain("TopSecret1", artifact.Content, StringComparison.OrdinalIgnoreCase));
        AssertConnectionFree(result);
    }
}
