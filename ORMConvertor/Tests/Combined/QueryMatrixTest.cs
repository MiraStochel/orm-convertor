using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The 3x3 query matrix through the real orchestration. Until now nothing exercised
/// <see cref="ConversionHandler"/>'s query path at all, so eight of the nine directions could
/// drop their input in silence without a single test noticing.
/// </summary>
public class QueryMatrixTest
{
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
    public void EveryDirectionProducesAQueryArtifact(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source));

        var query = result.Sources.Where(s => s.ContentType.IsQuery()).ToList();

        Assert.NotEmpty(query);
        Assert.All(query, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Content)));
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public void NoDirectionDropsTheQueryInSilence(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, SourcesFor(source));

        // Whatever else happens, the two Failure records the orchestration used to leave
        // unsaid - no query builder, no query parser - must never be the outcome now that
        // every direction is covered (decision 022).
        Assert.DoesNotContain(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("has no query"));
    }

    [Theory]
    [InlineData(ORMEnum.Dapper, "SELECT")]
    [InlineData(ORMEnum.EFCore, "ctx.Set<")]
    [InlineData(ORMEnum.NHibernate, "from ")]
    public void EachTargetEmitsItsOwnQueryLanguage(ORMEnum target, string hallmark)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, SourcesFor(ORMEnum.EFCore));

        var query = result.Sources.First(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(hallmark, query);
    }

    /// <summary>
    /// Decision 025: a language whose target form is a string is emitted bare as well, so no
    /// consumer has to extract it from the surrounding C#.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery)]
    [InlineData(ORMEnum.NHibernate, ConversionContentType.HqlQuery)]
    public void StringLanguagesAreAlsoEmittedBare(ORMEnum target, ConversionContentType expected)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, SourcesFor(ORMEnum.EFCore));

        Assert.Contains(result.Sources, s => s.ContentType == expected);
    }

    /// <summary>A blank query box is not a claim, so it produces neither artifact nor record.</summary>
    [Fact]
    public void ABlankQuerySourceIsNotAFailure()
    {
        List<ConversionSource> sources =
        [
            new() { Content = EFCoreEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = "   ", ContentType = ConversionContentType.CSharpQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, sources);

        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A set operation through the real orchestration: the parser reads the UNION, the
    /// entity maps travel to the target builder, and the LINQ target composes with Union.
    /// </summary>
    [Fact]
    public void AUnionTravelsThroughTheOrchestration()
    {
        const string unionQuery = """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT c.CustomerName FROM Sales.Customers AS c
            """;

        List<ConversionSource> sources =
        [
            new() { Content = DapperEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = unionQuery, ContentType = ConversionContentType.SqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, sources);

        var query = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(".Union(", query);
        Assert.Contains("ctx.Set<Customer>()", query);
    }

    /// <summary>
    /// The source framework has no parser for this language, and that has to be said out loud
    /// - it was the silent `continue` decision 022 removed.
    /// </summary>
    [Fact]
    public void AQueryLanguageTheSourceCannotReadIsReported()
    {
        List<ConversionSource> sources =
        [
            new() { Content = EFCoreEntity, ContentType = ConversionContentType.CSharpEntity },
            new() { Content = "from Customer c", ContentType = ConversionContentType.HqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, sources);

        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("HqlQuery"));
    }
}
