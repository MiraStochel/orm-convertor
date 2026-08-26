using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using DapperWrappers;
using EFCoreWrappers;
using Model;
using Model.AbstractRepresentation.Enums;
using NHibernateWrappers;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// Diagnostics as returned data (decision 010): the conversion returns records next to the
/// artifacts instead of throwing or staying silent. Completeness is checked against the
/// descriptor before generation - a failed check refuses the entity - and losses are
/// recorded at emission, mechanically from the descriptor where possible.
/// </summary>
public class DiagnosticsTest
{
    [Fact]
    public void EntityWithoutAKeyIsRefusedByNHibernateWithAFailureRecord()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "CustomerNumber", "public", hasGetter: true, hasSetter: true);

        var outputs = builder.Build();

        // A <class> element has to carry an identifier, so the gate refuses the entity
        // before half an artifact is written - and says so instead of failing later.
        Assert.Empty(outputs);
        var record = Assert.Single(builder.Records);
        Assert.Equal(ConversionRecordKind.Failure, record.Kind);
        Assert.Equal(ORMEnum.NHibernate, record.Framework);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal(MappingFactCategory.PrimaryKey, record.Category);
    }

    [Fact]
    public void PropertyWithoutALanguageTypeIsAFailureRecordNotAnException()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddTable("Orders");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");

        // A property only the mapping knows: no entity class declared it, so it has no
        // language type. Generating it used to end in NotSupportedException mid-artifact.
        builder.SetPropertyDatabaseMapping("Ghost", new Dictionary<string, string> { ["column"] = "GhostColumn" });

        var outputs = builder.Build();

        Assert.Empty(outputs);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Failure);
        Assert.Equal("Order", record.Entity);
        Assert.Equal("Ghost", record.Property);
    }

    [Fact]
    public void DapperReportsEveryFactItCannotExpressAndStillGenerates()
    {
        var builder = new DapperEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "Id");
        builder.SetPropertyDatabaseMapping("Id", new Dictionary<string, string> { ["column"] = "CustomerID" });

        var outputs = builder.Build();

        // A loss is a successful conversion: the artifact exists and is valid, only poorer.
        Assert.Single(outputs);

        // The records follow mechanically from the descriptor: every fact the model carries
        // in a NotExpressible category surfaces, none is written by hand in the builder.
        Assert.All(builder.Records, r => Assert.Equal(ConversionRecordKind.Loss, r.Kind));
        Assert.Contains(builder.Records, r => r.Category == MappingFactCategory.TableName);
        Assert.Contains(builder.Records, r => r.Category == MappingFactCategory.PrimaryKey);
        Assert.Contains(builder.Records, r => r.Category == MappingFactCategory.ColumnName && r.Property == "Id");
        Assert.Contains(builder.Records, r => r.Category == MappingFactCategory.PrimaryKeyStrategy && r.Property == "Id");
    }

    [Fact]
    public void FluentOnlyStrategyIsALossForEFCoreAnnotations()
    {
        var builder = new EFCoreEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddTable("Orders");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Sequence, "OrderID");

        var code = builder.Build().Single().Content;

        // The annotation form has no way to name a sequence; the strategy is dropped and
        // the drop is recorded rather than silent (decision 011).
        Assert.DoesNotContain("DatabaseGenerated", code);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
        Assert.Equal("OrderID", record.Property);
    }

    [Fact]
    public void CompositeIdDropsThePartStrategyAndRecordsIt()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "OrderLine");
        builder.AddTable("OrderLines");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddProperty("int", "LineNumber", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(
        [
            ("OrderID", 1, PrimaryKeyStrategy.Identity),
            ("LineNumber", 2, PrimaryKeyStrategy.Assigned),
        ]);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // The artifact is generated with assigned semantics; the Identity of the first part
        // cannot survive because <composite-id> admits no generator.
        Assert.Contains("<composite-id>", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
        Assert.Equal("OrderID", record.Property);
    }

    [Fact]
    public void ZeroPrecisionIsDroppedForNHibernateWithALossRecord()
    {
        var builder = new NHibernateEntityBuilder();
        new EFCoreEntityParser(builder).Parse("""
            public class Order
            {
                [Key]
                public required int OrderID { get; set; }

                [Precision(0)]
                public required DateTime OrderDate { get; set; }
            }
            """);

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // NHibernate's mapping schema admits only a positive precision, so zero - the
        // sub-second precision of a date-time column - would make the framework refuse the
        // whole document. The fact is dropped and the drop recorded (decision 004); the
        // acceptance level of decision 016 is what caught this.
        Assert.DoesNotContain("precision", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal(MappingFactCategory.PrecisionAndScale, record.Category);
        Assert.Equal("OrderDate", record.Property);
    }

    [Fact]
    public void UnstatedStrategyWrittenAsAssignedIsAConventionOfTheTarget()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Customer");
        builder.AddTable("Customers");
        builder.AddProperty("int", "Id", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "Id");

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // assigned is what NHibernate assumes, not what the source said - the mapping is
        // usable, but the claim is the target's and the record says so.
        Assert.Contains("<generator class=\"assigned\" />", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Convention);
        Assert.Equal(MappingFactCategory.PrimaryKeyStrategy, record.Category);
    }

    [Fact]
    public void SourceGeneratorNameTheTargetDoesNotKnowIsALoss()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddTable("Orders");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Unspecified, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", "OrderIds.SnowflakeGenerator, OrderIds");

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // A name outside the target's own list of generators is never written back - the
        // artifact would fail to start on an unloadable type - so the canonical fallback goes
        // out and the record says why the name was dropped (decision 021).
        Assert.Contains("<generator class=\"assigned\" />", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains("SnowflakeGenerator", record.Reason);
        Assert.Contains("does not know", record.Reason);
    }

    [Fact]
    public void GeneratorParameterTheTargetCannotExpressIsALoss()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddTable("Orders");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.HiLo, "OrderID");
        builder.SetKeyStrategyDetails("OrderID", parameters: new Dictionary<GeneratorParameter, string>
        {
            [GeneratorParameter.CounterTable] = "hibernate_unique_key",
            [GeneratorParameter.CounterKeyColumn] = "sequence_name",
        });

        var xml = builder.Build().Single(o => o.ContentType == ConversionContentType.XML).Content;

        // NHibernate's hilo keeps a single-row counter table: the JPA-style row selector has
        // no counterpart there, so it goes out as a record, not silently (decision 020).
        Assert.Contains("<generator class=\"hilo\">", xml);
        Assert.Contains("<param name=\"table\">hibernate_unique_key</param>", xml);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Contains(nameof(GeneratorParameter.CounterKeyColumn), record.Reason);
    }

    [Fact]
    public void TargetOutsideTheConversionIsRecordedAsIncompleteness()
    {
        var builder = new NHibernateEntityBuilder();
        builder.AddClassHeader("public", "Order");
        builder.AddTable("Orders");
        builder.AddProperty("int", "OrderID", "public", hasGetter: true, hasSetter: true);
        builder.AddPrimaryKey(PrimaryKeyStrategy.Identity, "OrderID");
        builder.AddProperty("object", "Customer", "public", hasGetter: true, hasSetter: true);
        builder.AddForeignKey(Cardinality.ManyToOne, "Customer", "Customer");

        var outputs = builder.Build();

        // Generation goes on - the reference may legitimately point outside the conversion,
        // and only the database catalog could tell that apart from a typo (decision 015).
        Assert.NotEmpty(outputs);
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Incompleteness);
        Assert.Equal("Order", record.Entity);
        Assert.Equal("Customer", record.Property);
        Assert.Contains("not part of the conversion", record.Reason);
    }

    [Fact]
    public void PropertyRefValueTheModelCannotKeepIsReportedByTheParser()
    {
        var builder = new NHibernateEntityBuilder();
        new NHibernateXMLMappingParser(builder).Parse("""
            <?xml version="1.0" encoding="utf-8" ?>
            <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                <class name="Customer" table="Customers">
                    <id name="CustomerID" column="CustomerID" type="Int32">
                        <generator class="identity" />
                    </id>
                    <one-to-one name="Profile" class="CustomerProfile" property-ref="Owner" />
                </class>
            </hibernate-mapping>
            """);

        // No Build here: the value is dropped on the way into the model, so the parser is
        // the only place that still sees it and has to be the one reporting it.
        var record = Assert.Single(builder.Records, r => r.Kind == ConversionRecordKind.Loss);
        Assert.Equal("Customer", record.Entity);
        Assert.Equal("Profile", record.Property);
        Assert.Contains("Owner", record.Reason);
    }

    [Fact]
    public void ConversionReturnsRecordsNextToTheArtifacts()
    {
        var sources = new List<ConversionSource>
        {
            new()
            {
                ContentType = ConversionContentType.CSharpEntity,
                Content = """
                    public class Customer
                    {
                        public virtual int CustomerID { get; set; }
                    }
                    """,
            },
            new()
            {
                ContentType = ConversionContentType.XML,
                Content = """
                    <?xml version="1.0" encoding="utf-8" ?>
                    <hibernate-mapping xmlns="urn:nhibernate-mapping-2.2">
                        <class name="Customer" table="Customers">
                            <id name="CustomerID" column="CustomerID" type="Int32">
                                <generator class="identity" />
                            </id>
                        </class>
                    </hibernate-mapping>
                    """,
            },
        };

        var result = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.Dapper, sources);

        // The channel decision 010 asked for: artifacts and records side by side in one
        // returned value, all the way out of the orchestration.
        Assert.NotEmpty(result.Sources);
        Assert.NotEmpty(result.Records);
        Assert.All(result.Records, r => Assert.Equal(ORMEnum.Dapper, r.Framework));
        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Loss && r.Category == MappingFactCategory.PrimaryKey);
    }
}

/// <summary>
/// A conversion that produced nothing says so (decision 045). Silence used to be the answer
/// to input the parsers could not read: no artifact, no record, status 200 - a "done" about
/// something that never happened. The same class of defect had already been closed one level
/// up, where an unknown source framework used to return an empty result and no error at all.
/// </summary>
public class EmptyConversionTest
{
    private static List<ConversionSource> Entity(string content) =>
        [new() { ContentType = ConversionContentType.CSharpEntity, Content = content }];

    /// <summary>
    /// Roslyn parses almost anything, so text that is not a class comes back as a parse tree
    /// with no entity in it. Two records on purpose: the unit's own (decision 066) and the
    /// run's (decision 045) - one says this unit yielded nothing, the other that the whole
    /// run generated nothing, and they answer different questions.
    /// </summary>
    [Fact]
    public void InputWithNoEntityInItIsReportedInsteadOfIgnored()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, Entity("this is not C#"));

        Assert.Empty(result.Sources);
        Assert.Equal(2, result.Records.Count);
        Assert.All(result.Records, r =>
        {
            Assert.Equal(ConversionRecordKind.Failure, r.Kind);
            Assert.Equal(ORMEnum.NHibernate, r.Framework);
        });
        Assert.Contains(result.Records, r => r.Unit == "unit 1" && r.Reason.Contains("came of it"));
        Assert.Contains(result.Records, r => r.Unit == null && r.Reason.Contains("yielded"));
    }

    /// <summary>
    /// An empty request is the other half of the same silence, and it gets its own wording:
    /// "nothing came in" and "nothing came out of what came in" are different messages.
    /// </summary>
    [Fact]
    public void ARequestWithoutUnitsIsReportedAsSuch()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, []);

        Assert.Empty(result.Sources);
        var record = Assert.Single(result.Records);
        Assert.Equal(ConversionRecordKind.Failure, record.Kind);
        Assert.Contains("no source unit", record.Reason);
    }

    /// <summary>
    /// A unit in a language the source framework has no parser for used to fall through the
    /// parser loop without a word - the loop asks parsers what they accept and never asks who
    /// claimed nothing. Dapper reads C# entities and SQL queries, not hbm mapping.
    /// </summary>
    [Fact]
    public void AUnitInALanguageTheSourceCannotReadIsReported()
    {
        var sources = new List<ConversionSource>
        {
            new() { ContentType = ConversionContentType.XML, Content = "<hibernate-mapping />" },
        };

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, sources);

        Assert.Contains(result.Records, r =>
            r.Kind == ConversionRecordKind.Failure
            && r.Artifact == ConversionContentType.XML
            && r.Reason.Contains("no parser"));
    }

    /// <summary>
    /// An unfilled input box is not a claim (decision 025), so a blank unit stays silent - the
    /// records must not fill up with what the user simply did not type. The run itself is still
    /// reported as having generated nothing.
    /// </summary>
    [Fact]
    public void ABlankUnitIsSkippedWithoutARecordOfItsOwn()
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, Entity("   "));

        var record = Assert.Single(result.Records);
        Assert.Contains("no source unit", record.Reason);
    }

    /// <summary>
    /// The record is written only when the run produced nothing: a conversion that generated
    /// artifacts must not carry a failure about itself.
    /// </summary>
    [Fact]
    public void AConversionThatProducedArtifactsCarriesNoSuchRecord()
    {
        var result = ConversionHandler.Convert(
            ORMEnum.EFCore,
            ORMEnum.NHibernate,
            Entity(SampleData.CustomerSampleEFCore.Entity));

        Assert.NotEmpty(result.Sources);
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("nothing was generated"));
        Assert.DoesNotContain(result.Records, r => r.Reason.Contains("yielded"));
    }
}
