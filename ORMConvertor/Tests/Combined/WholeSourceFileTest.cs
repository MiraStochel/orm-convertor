using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// A unit is a whole source file in one language with whatever it holds (decision 111): the
/// source framework reads the entities and the queries out of it, and every class it does not
/// read as an entity - the code around queries, the framework's own API, a class without an
/// instance - says so in a record. What the query pass reads as a handover the entity pass
/// does not read as an entity, because both ask the same search.
/// </summary>
public class WholeSourceFileTest
{
    private const string File = "Customers.cs";

    private static ConversionResult Convert(ORMEnum source, ORMEnum target, ConversionContentType language, string content, string name = File)
        => ConversionHandler.Convert(source, target, [new() { ContentType = language, Content = content, Name = name }]);

    private static List<string> Artifacts(ConversionResult result, ConversionContentType type)
        => [.. result.Sources.Where(s => s.ContentType == type).Select(s => s.Content)];

    private static List<ConversionRecord> NotEntities(ConversionResult result)
        => [.. result.Records.Where(r => r.Reason.Contains("not as an entity", StringComparison.Ordinal)
            || r.Reason.Contains("not read as an entity", StringComparison.Ordinal)
            || r.Reason.Contains("is the EF Core context", StringComparison.Ordinal))];

    private const string DapperFile = """
        namespace Shop;

        public class Customer
        {
            public int CustomerID { get; set; }

            public string CustomerName { get; set; } = "";

            public decimal CreditLimit { get; set; }
        }

        public class CustomerRepository(IDbConnection connection)
        {
            public IEnumerable<Customer> Rich()
                => connection.Query<Customer>("SELECT c.CustomerID, c.CustomerName FROM Customers AS c WHERE c.CreditLimit > 2000");

            public IEnumerable<Customer> Ordered()
                => connection.Query<Customer>("SELECT c.CustomerID, c.CustomerName FROM Customers AS c ORDER BY c.CustomerName ASC");
        }
        """;

    [Fact]
    public void AFileWithAnEntityAndADapperRepositoryYieldsTheEntityAndEveryQuery()
    {
        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, ConversionContentType.CSharp, DapperFile);

        var entity = Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("class Customer", entity, StringComparison.Ordinal);

        var queries = Artifacts(result, ConversionContentType.CSharpQuery);
        Assert.Equal(2, queries.Count);
        Assert.Contains(queries, q => q.Contains("Query01", StringComparison.Ordinal) && q.Contains("2000", StringComparison.Ordinal));
        Assert.Contains(queries, q => q.Contains("Query02", StringComparison.Ordinal) && !q.Contains("2000", StringComparison.Ordinal));

        var record = Assert.Single(NotEntities(result));
        Assert.Equal(ConversionRecordKind.Convention, record.Kind);
        Assert.Equal(File, record.Unit);
        Assert.Contains("'CustomerRepository'", record.Reason, StringComparison.Ordinal);
        Assert.Contains("'Rich'", record.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithAnEntityAndALinqRepositoryYieldsTheEntityAndEveryQuery()
    {
        const string file = """
            namespace Shop;

            public class Customer
            {
                public int CustomerID { get; set; }

                public string CustomerName { get; set; } = "";

                public decimal CreditLimit { get; set; }
            }

            public class CustomerRepository(ShopContext ctx)
            {
                public List<Customer> Rich() => ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();

                public List<Customer> Ordered() => ctx.Customers.OrderBy(c => c.CustomerName).ToList();
            }
            """;

        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, file);

        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Equal(2, Artifacts(result, ConversionContentType.SqlQuery).Count);
        Assert.Contains("'CustomerRepository'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    [Fact]
    public void AnNHibernateFileWithASessionQueryYieldsTheEntityAndTheQuery()
    {
        const string file = """
            namespace Shop;

            public class Customer
            {
                public virtual int CustomerID { get; set; }

                public virtual decimal CreditLimit { get; set; }
            }

            public class CustomerQueries(ISession session)
            {
                public IList<Customer> Rich() => session.Query<Customer>().Where(c => c.CreditLimit > 2000).ToList();
            }
            """;

        var result = Convert(ORMEnum.NHibernate, ORMEnum.Dapper, ConversionContentType.CSharp, file);

        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Contains("'CustomerQueries'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void AJavaFileWithAnEntityAndACreateQueryYieldsTheEntityAndTheQuery(ORMEnum source)
    {
        const string file = """
            package shop;

            import jakarta.persistence.Entity;
            import jakarta.persistence.EntityManager;
            import jakarta.persistence.Id;
            import jakarta.persistence.Table;
            import java.util.List;

            @Entity
            @Table(name = "Customers")
            public class Customer {

                @Id
                private Integer CustomerID;

                private String CustomerName;
            }

            class CustomerQueries {

                public List<Customer> named(EntityManager em) {
                    return em.createQuery("select c from Customer c where c.CustomerName = 'x'", Customer.class).getResultList();
                }
            }
            """;

        var result = Convert(source, ORMEnum.EFCore, ConversionContentType.Java, file, "Customer.java");

        var entity = Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("class Customer", entity, StringComparison.Ordinal);
        Assert.Single(Artifacts(result, ConversionContentType.CSharpQuery));

        var record = Assert.Single(NotEntities(result));
        Assert.Equal(ConversionRecordKind.Convention, record.Kind);
        Assert.Contains("'CustomerQueries'", record.Reason, StringComparison.Ordinal);
        Assert.Contains("'named'", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The context is EF Core's API, not an entity: it is left out with a loss, a query over
    /// its own DbSet inside it is read like one over ctx.Customers from beside it, and the
    /// class beside it is the code around a query.
    /// </summary>
    [Fact]
    public void TheContextIsNoEntityAndAQueryOverItsOwnDbSetIsRead()
    {
        const string file = """
            namespace Shop;

            public class ShopContext : DbContext
            {
                public DbSet<Customer> Customers { get; set; } = null!;

                public List<Customer> Rich() => Customers.Where(c => c.CreditLimit > 2000).ToList();
            }

            public class Customer
            {
                public int CustomerID { get; set; }

                public string CustomerName { get; set; } = "";

                public decimal CreditLimit { get; set; }
            }

            public class Reports(ShopContext ctx)
            {
                public List<Customer> Ordered() => ctx.Customers.OrderBy(c => c.CustomerName).ToList();
            }
            """;

        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, file);

        var entity = Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("class Customer", entity, StringComparison.Ordinal);

        var queries = Artifacts(result, ConversionContentType.SqlQuery);
        Assert.Equal(2, queries.Count);
        Assert.Contains(queries, q => q.Contains("2000", StringComparison.Ordinal));

        var records = NotEntities(result);
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Loss && r.Reason.Contains("'ShopContext'", StringComparison.Ordinal));
        Assert.Contains(records, r => r.Kind == ConversionRecordKind.Convention && r.Reason.Contains("'Reports'", StringComparison.Ordinal));
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void AContextRecognizedByItsDbSetAloneIsNoEntityEither()
    {
        const string file = """
            public class Customer
            {
                public int CustomerID { get; set; }
            }

            public class Shop
            {
                public DbSet<Customer> Customers { get; set; } = null!;
            }
            """;

        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, file);

        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Equal(ConversionRecordKind.Loss, Assert.Single(NotEntities(result)).Kind);
    }

    [Fact]
    public void AStaticClassIsNoEntity()
    {
        const string file = """
            public class Customer
            {
                public int CustomerID { get; set; }
            }

            public static class Formatting
            {
                public static string Upper(string text) => text.ToUpperInvariant();
            }
            """;

        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, ConversionContentType.CSharp, file);

        Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        var record = Assert.Single(NotEntities(result));
        Assert.Equal(ConversionRecordKind.Convention, record.Kind);
        Assert.Contains("'Formatting' is static", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// LINQ over the entity's own collection walks rows the instance loaded, which no provider
    /// translates: the entity stays an entity and no query comes of it.
    /// </summary>
    [Fact]
    public void ANavigationOverTheEntitysOwnCollectionIsNoQuery()
    {
        const string file = """
            public class SalesOrder
            {
                public int SalesOrderID { get; set; }

                public List<OrderLine> Lines { get; set; } = [];

                public decimal Total() => Lines.Sum(l => l.Amount);

                public int Count() => this.Lines.Count();
            }

            public class OrderLine
            {
                public int OrderLineID { get; set; }

                public int SalesOrderID { get; set; }

                public decimal Amount { get; set; }
            }
            """;

        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, file);

        Assert.Equal(2, Artifacts(result, ConversionContentType.CSharpEntity).Count);
        Assert.Empty(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.Empty(NotEntities(result));
    }

    /// <summary>A handover belongs to the class whose own member it stands in; a class nested in a repository is a class of its own.</summary>
    [Fact]
    public void AClassNestedInTheRepositoryStaysAnEntity()
    {
        const string file = """
            public class CustomerRepository(IDbConnection connection)
            {
                public class NameRow
                {
                    public string CustomerName { get; set; } = "";
                }

                public IEnumerable<NameRow> Names() => connection.Query<NameRow>("SELECT c.CustomerName FROM Customers AS c");
            }
            """;

        var result = Convert(ORMEnum.Dapper, ORMEnum.EFCore, ConversionContentType.CSharp, file);

        var entity = Assert.Single(Artifacts(result, ConversionContentType.CSharpEntity));
        Assert.Contains("class NameRow", entity, StringComparison.Ordinal);
        Assert.Contains("'CustomerRepository'", Assert.Single(NotEntities(result)).Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fragment stays a unit (decision 111): a bare method - the shape the query builders
    /// write - is read for its query, and the entity pass, which reads it too, finds no class
    /// and says nothing.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.EFCore, ConversionContentType.CSharp, "public List<Customer> Query() => ctx.Customers.Where(c => c.CreditLimit > 2000).ToList();")]
    [InlineData(ORMEnum.Hibernate, ConversionContentType.Java, "public List<Customer> query(EntityManager em) { return em.createQuery(\"select c from Customer c where c.CreditLimit > 2000\", Customer.class).getResultList(); }")]
    public void AFragmentIsReadForItsQueryAndTheEntityPassIsSilent(ORMEnum source, ConversionContentType language, string fragment)
    {
        var result = ConversionHandler.Convert(source, ORMEnum.Dapper,
        [
            .. CrossFrameworkInputs.MappingUnits(source),
            new() { ContentType = language, Content = fragment, Name = "Query" },
        ]);

        Assert.Single(Artifacts(result, ConversionContentType.SqlQuery));
        Assert.DoesNotContain(result.Records, r => r.Unit == "Query");
    }

    [Fact]
    public void AFileWithNeitherAClassNorAHandoverIsOneBarrenUnit()
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, "int answer = 42;");

        var record = Assert.Single(result.Records, r => r.Unit == File);
        Assert.Equal(ConversionRecordKind.Failure, record.Kind);
        Assert.Contains("neither a mapping fact nor a query", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>A role value is an artifact's, and the record says which language value the unit should declare.</summary>
    [Theory]
    [InlineData(ConversionContentType.CSharpEntity, "CSharp")]
    [InlineData(ConversionContentType.CSharpQuery, "CSharp")]
    public void ARoleValueOnInputIsNotReadAndTheRecordNamesTheLanguage(ConversionContentType roleValue, string language)
    {
        var result = Convert(ORMEnum.EFCore, ORMEnum.Dapper, roleValue, DapperFile);

        Assert.Empty(result.Sources);
        var record = Assert.Single(result.Records, r => r.Unit == File);
        Assert.Contains("has no parser", record.Reason, StringComparison.Ordinal);
        Assert.Contains($"declares its language, {language}", record.Reason, StringComparison.Ordinal);
    }

    /// <summary>The C# file of the samples carries both halves, which is what the translation screen loads.</summary>
    [Fact]
    public void TheCompositeSampleFilesReadAsTheirParts()
    {
        var efcore = Convert(ORMEnum.EFCore, ORMEnum.Dapper, ConversionContentType.CSharp, SampleData.CustomerSampleEFCore.Source);
        Assert.Single(Artifacts(efcore, ConversionContentType.CSharpEntity));
        Assert.Single(Artifacts(efcore, ConversionContentType.SqlQuery));

        var mybatis = ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.EFCore,
        [
            new() { ContentType = ConversionContentType.Java, Content = SampleData.CustomerSampleMyBatis.Source },
            new() { ContentType = ConversionContentType.XML, Content = SampleData.CustomerSampleMyBatis.XmlMapper },
        ]);

        var split = ConversionHandler.Convert(ORMEnum.MyBatis, ORMEnum.EFCore,
        [
            new() { ContentType = ConversionContentType.Java, Content = SampleData.CustomerSampleMyBatis.Entity },
            new() { ContentType = ConversionContentType.Java, Content = SampleData.CustomerSampleMyBatis.MapperInterface },
            new() { ContentType = ConversionContentType.XML, Content = SampleData.CustomerSampleMyBatis.XmlMapper },
        ]);

        Assert.Equal(split.Sources.Select(s => s.Content), mybatis.Sources.Select(s => s.Content));
    }
}
