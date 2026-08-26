using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Combined;

/// <summary>
/// Records attributed to input units (decision 066). The last silent case of decision 045
/// was a unit nothing came of beside a productive one - the caller got output and never
/// heard about the unit it sent. The parser now states what it read from each unit, and a
/// record born from one unit's reading names that unit: by the client-given name, or by
/// "unit N" with the unit's 1-based position in the request.
/// </summary>
public class UnitAttributionTest
{
    private const string CustomerEntity = """
        public class Customer
        {
            public virtual int CustomerID { get; set; }
        }
        """;

    private const string CustomerMapping = """
        <?xml version="1.0" encoding="utf-8" ?>
        <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
            <class name="Customer" table="Customers">
                <id name="CustomerID" column="CustomerID" type="Int32">
                    <generator class="identity" />
                </id>
            </class>
        </hibernate-mapping>
        """;

    [Fact]
    public void AUnitThatYieldsNothingBesideAProductiveOneIsReported()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerSampleEFCore.Entity },
            new() { ContentType = ConversionContentType.CSharpEntity, Content = "this is not C#" },
        };

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, sources);

        // The run produced artifacts, so the run-level record of decision 045 stays away -
        // and the barren unit still gets its own.
        Assert.NotEmpty(result.Sources);
        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("unit 2", record.Unit);
        Assert.Equal(ConversionContentType.CSharpEntity, record.Artifact);
        Assert.Contains("came of it", record.Reason);
    }

    [Fact]
    public void TheRecordCarriesTheClientGivenName()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerSampleEFCore.Entity },
            new() { ContentType = ConversionContentType.CSharpEntity, Content = "this is not C#", Name = "Broken.cs" },
        };

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, sources);

        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Broken.cs", record.Unit);
    }

    /// <summary>
    /// The trap that rules out counting maps around the parser (decisions 045 and 066): the
    /// NHibernate XML parser enriches the entity the class parser already created, so it adds
    /// no new map - and the unit yielded plenty. Enrichment counts the same as creation.
    /// </summary>
    [Fact]
    public void AnEnrichingMappingUnitIsNotCalledBarren()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerEntity },
            new() { ContentType = ConversionContentType.XML, Content = CustomerMapping },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore, sources);

        Assert.NotEmpty(result.Sources);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("came of it"));
    }

    /// <summary>
    /// A mapping document with a foreign root is well-formed XML the parser opens and walks
    /// away from - it used to vanish without a word beside a productive unit.
    /// </summary>
    [Fact]
    public void AMappingUnitWithAForeignRootIsReported()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerEntity },
            new() { ContentType = ConversionContentType.XML, Content = "<entity-mappings />", Name = "orm.xml" },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore, sources);

        Assert.NotEmpty(result.Sources);
        var record = Assert.Single(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("orm.xml", record.Unit);
        Assert.Contains("came of it", record.Reason);
    }

    /// <summary>
    /// A record born while a unit is being read names that unit: the reading is its origin.
    /// Records about the merged entity or from generation carry no unit, because several
    /// units may legitimately have declared the entity.
    /// </summary>
    [Fact]
    public void ARecordBornWhileReadingAUnitNamesThatUnit()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerEntity, Name = "Customer.cs" },
            new()
            {
                ContentType = ConversionContentType.XML,
                Name = "Customer.hbm.xml",
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="Customer" table="Customers">
                            <id name="CustomerID" column="CustomerID" type="Int32">
                                <generator class="identity" />
                            </id>
                            <one-to-one name="Profile" class="CustomerProfile" property-ref="Owner" />
                        </class>
                    </hibernate-mapping>
                    """,
            },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore, sources);

        // The property-ref value has no place in the model and the XML parser reports the
        // drop while reading - so the record points at the mapping file, not just the entity.
        var loss = Assert.Single(result.Records, r => r.Reason.Contains("property-ref"));
        Assert.Equal("Customer.hbm.xml", loss.Unit);
        Assert.Equal("Customer", loss.Entity);
    }

    /// <summary>
    /// The query branch scopes a builder to one unit, so every record it holds belongs to
    /// that unit - two failed queries stop being two anonymous records.
    /// </summary>
    [Fact]
    public void QueryRecordsCarryTheUnitOfTheirQuery()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.CSharpEntity, Content = CustomerSampleEFCore.Entity, Name = "Customer.cs" },
            new() { ContentType = ConversionContentType.CSharpQuery, Content = "int x = 5;", Name = "Query.cs" },
        };

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, sources);

        var record = Assert.Single(result.Records, r => r.Reason.Contains("No LINQ query chain"));
        Assert.Equal("Query.cs", record.Unit);
    }
}
