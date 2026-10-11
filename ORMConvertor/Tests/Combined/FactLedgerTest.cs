using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;
using Model.AbstractRepresentation.Enums;
using OrmConvertor;
using SampleData;
using Tests.Catalog;

namespace Tests.Combined;

/// <summary>
/// The fact ledger and the origin of artifacts (decision 124). The records of decision 010
/// say what did not travel; a fact that travelled had no sentence, so input and output could
/// not be paired honestly. Now every artifact names the entity it was generated for and the
/// units that declared it, and every mapping fact of the representation has a row saying
/// which unit stated it - or that the catalog supplied it or the build derived it - and
/// whether the artifact carries it. All of it is written by the builder base and the
/// orchestration; no parser and no builder says a word, which is what keeps a new framework
/// free of it (S1).
/// </summary>
public class FactLedgerTest
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

    private static ConversionSource Unit(string name, ConversionContentType type, string content)
        => new() { Name = name, ContentType = type, Content = content };

    private static ConversionFact FactOf(ConversionResult result, string entity, string? property, MappingFactCategory category)
        => Assert.Single(result.Facts, f => f.Entity == entity && f.Property == property && f.Category == category);

    /// <summary>
    /// A fact the input stated and the target expresses: the ledger names the unit and says
    /// the artifact carries it, with the value a reader can find in both texts.
    /// </summary>
    [Fact]
    public void AStatedFactTheTargetExpressesIsCarriedFromItsUnit()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            [Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Entity)]);

        var length = FactOf(result, "Customer", "CustomerName", MappingFactCategory.Length);
        Assert.Equal("200", length.Value);
        Assert.Equal(FactOrigin.Source, length.Origin);
        Assert.Equal("Customer.cs", length.Unit);
        Assert.Equal(FactOutcome.Carried, length.Outcome);

        var table = FactOf(result, "Customer", null, MappingFactCategory.TableName);
        Assert.Equal("Customers", table.Value);
        Assert.Equal(FactOrigin.Source, table.Origin);
        Assert.Equal(FactOutcome.Carried, table.Outcome);
    }

    /// <summary>
    /// The same fact into a target with no place for it is dropped - the same verdict the Loss
    /// record of decision 004 already gives, read off the descriptor, so the ledger cannot
    /// claim a fact the artifact does not hold.
    /// </summary>
    [Fact]
    public void AStatedFactTheTargetCannotExpressIsDropped()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.Dapper,
            [Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Entity)]);

        var length = FactOf(result, "Customer", "CustomerName", MappingFactCategory.Length);
        Assert.Equal(FactOutcome.Dropped, length.Outcome);
        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Entity == "Customer" && r.Property == "CustomerName" && r.Category == MappingFactCategory.Length);
    }

    /// <summary>
    /// An artifact names its entity and the units that declared it. The NHibernate pair is the
    /// case that rules out any one-unit answer: the class and the mapping document both speak
    /// about the entity, and the artifacts of the entity name both (decision 066).
    /// </summary>
    [Fact]
    public void AnArtifactNamesItsEntityAndEveryUnitThatDeclaredIt()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.EFCore,
            [
                Unit("Customer.cs", ConversionContentType.CSharp, CustomerEntity),
                Unit("Customer.hbm.xml", ConversionContentType.XML, CustomerMapping),
            ]);

        var artifact = Assert.Single(result.Sources);
        Assert.Equal("Customer", artifact.Entity);
        Assert.Equal(["Customer.cs", "Customer.hbm.xml"], artifact.Units);

        // The table is the mapping document's statement, not the class's - the ledger says so
        // because the fact first stood in the representation after that unit was read.
        var table = FactOf(result, "Customer", null, MappingFactCategory.TableName);
        Assert.Equal("Customer.hbm.xml", table.Unit);
        Assert.Equal(FactOrigin.Source, table.Origin);
    }

    /// <summary>
    /// A fact the catalog supplied has the catalog as its origin, matched through the Supplied
    /// record the completion phase writes - the record stays the fuller statement (decision
    /// 015), the ledger only points at it.
    /// </summary>
    [Fact]
    public void AFactTheCatalogSuppliedHasTheCatalogAsItsOrigin()
    {
        const string dapperEntity = """
            namespace DapperEntities;

            public class Customer
            {
                public int CustomerId { get; set; }

                public string Name { get; set; } = string.Empty;
            }
            """;

        var image = new TableImage
        {
            Schema = "sales",
            Name = "Customers",
            Columns =
            [
                new ColumnImage { Name = "CustomerId", Type = DatabaseType.Integer, IsNullable = false, IsIdentity = true },
                new ColumnImage { Name = "Name", Type = DatabaseType.VarChar, IsUnicode = true, Length = 100, IsNullable = false, IsIdentity = false },
            ],
            PrimaryKeyColumns = ["CustomerId"],
            ForeignKeys = [],
        };

        var result = ConversionHandler.Convert(
            ORMEnum.Dapper,
            ORMEnum.NHibernate,
            [new ConversionSource { ContentType = ConversionContentType.CSharp, Content = dapperEntity }],
            new FakeCatalogReader(image));

        var table = FactOf(result, "Customer", null, MappingFactCategory.TableName);
        Assert.Equal(FactOrigin.Catalog, table.Origin);
        Assert.Null(table.Unit);
        Assert.Equal(FactOutcome.Carried, table.Outcome);

        var length = FactOf(result, "Customer", "Name", MappingFactCategory.Length);
        Assert.Equal(FactOrigin.Catalog, length.Origin);
        Assert.Equal("100", length.Value);

        // The unit sent no name, so the artifact references it by position, as the records do.
        Assert.All(result.Sources, artifact => Assert.Equal(["unit 1"], artifact.Units));
    }

    /// <summary>
    /// A query artifact belongs to no entity and came from one unit; the orchestration says
    /// which, because the query loop is the one place that knows (decision 066).
    /// </summary>
    [Fact]
    public void AQueryArtifactNamesTheUnitItCameFromAndNoEntity()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            [
                Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Entity),
                Unit("Query.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Query),
            ]);

        var queries = result.Sources.Where(s => s.ContentType.IsQuery()).ToList();
        Assert.NotEmpty(queries);
        Assert.All(queries, query =>
        {
            Assert.Null(query.Entity);
            Assert.Equal(["Query.cs"], query.Units);
        });

        Assert.All(result.Sources.Where(s => !s.ContentType.IsQuery()), entity =>
            Assert.Equal(["Customer.cs"], entity.Units));
    }

    /// <summary>
    /// An entity the completeness gate refused has no artifact, so its facts have nothing to
    /// be carried into - and the ledger says that rather than calling them carried or dropped.
    /// </summary>
    [Fact]
    public void TheFactsOfARefusedEntityAreNotGenerated()
    {
        const string keyless = """
            namespace EFCoreEntities;

            using System.ComponentModel.DataAnnotations.Schema;

            [Table("Things")]
            public class Thing
            {
                public string Name { get; set; } = string.Empty;
            }
            """;

        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            [Unit("Thing.cs", ConversionContentType.CSharp, keyless)]);

        Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Entity == "Thing");
        Assert.DoesNotContain(result.Sources, s => s.Entity == "Thing");

        var facts = result.Facts.Where(f => f.Entity == "Thing").ToList();
        Assert.NotEmpty(facts);
        Assert.All(facts, fact => Assert.Equal(FactOutcome.NotGenerated, fact.Outcome));
    }

    /// <summary>
    /// The ledger is a function of the input, like everything else the conversion returns
    /// (S2): two runs over the same units agree row for row.
    /// </summary>
    [Fact]
    public void TheLedgerIsDeterministic()
    {
        ConversionResult Run() => ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            [Unit("Customer.cs", ConversionContentType.CSharp, CustomerSampleEFCore.Entity)]);

        Assert.Equal(Run().Facts, Run().Facts);
    }
}
