using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Combined;
using Tests.Foreign;

namespace Tests.Experiment;

/// <summary>
/// The measure of the completeness of transferred mapping facts itself (decision 173), held
/// over the tool's own artifacts of the scenario of <see cref="CrossFrameworkInputs"/> in every
/// direction: what the ledger says the artifact carries, the parser of the target reads back -
/// except the deviations recorded here by name, each with its reason, so that a new one fails
/// the test rather than lowering a share in silence. The negative half: an artifact whose column
/// was renamed is read back short of that fact, and the detail names it.
/// </summary>
public class MappingCompletenessTest(ITestOutputHelper output)
{
    /// <summary>
    /// Facts the ledger counts as carried and the read-back does not find, by direction, with
    /// why: the artifact does state the fact, but in a form the parser of the target reads
    /// into the representation differently, or not at all. Every entry is a finding about a
    /// parser or a builder of the tool and stands here until it is fixed.
    /// </summary>
    private static readonly Dictionary<(ORMEnum Source, ORMEnum Target), string[]> RecordedDeviations = new()
    {
        // MyBatis writes the key as the <id> of the result map, which its parser does not read
        // as a key: an <id> is a hint for the result cache, not a declared primary key, and a
        // MyBatis source states no key (decisions 063 and 084). The builder states more than the
        // parser reads, so the ledger carries what the read-back cannot see.
        [(ORMEnum.EFCore, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.NHibernate, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.Hibernate, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.EclipseLink, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],

        // The key column is not null by being the key: NHibernate's <id> and JPA's @Id carry no
        // not-null attribute, and no parser derives the nullability of a key from its being one.
        [(ORMEnum.EFCore, ORMEnum.NHibernate)] = ["Customer.CustomerId Nullability"],

        // The same, and the strategy: a source's Auto - the mechanism native to the dialect -
        // goes out as IDENTITY, the one GenerationType that names it on SQL Server (decision 187),
        // so the read-back states Identity where the reference states Auto.
        [(ORMEnum.EFCore, ORMEnum.Hibernate)] = ["Customer.CustomerId Nullability", "Customer.CustomerId PrimaryKeyStrategy"],
        [(ORMEnum.EFCore, ORMEnum.EclipseLink)] = ["Customer.CustomerId Nullability", "Customer.CustomerId PrimaryKeyStrategy"],

        // JPA states the database type of a column through the Java type, and in a literal
        // columnDefinition only where the vocabulary needs it; the parsers read no database type
        // out of int or BigDecimal, and out of String only the unicode facet, with no family.
        [(ORMEnum.NHibernate, ORMEnum.Hibernate)] = ["Customer.CreditLimit DatabaseType", "Customer.CustomerId DatabaseType", "Customer.CustomerName DatabaseType"],
        [(ORMEnum.NHibernate, ORMEnum.EclipseLink)] = ["Customer.CreditLimit DatabaseType", "Customer.CustomerId DatabaseType"],
    };

    public static TheoryData<ORMEnum, ORMEnum> Directions() => CrossFrameworkInputs.Directions();

    [Theory]
    [MemberData(nameof(Directions))]
    public void TheToolsArtifactReadsBackWhatTheLedgerSaysItCarries(ORMEnum source, ORMEnum target)
    {
        var conversion = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));
        if (conversion.Records.Any(record => record.Kind == ConversionRecordKind.Failure))
        {
            // A direction the tool refuses without a catalog (the source states no key) has no
            // artifact to measure; the experiment run writes it down as refused.
            return;
        }

        var artifact = Artifact(conversion, source, target);
        var measure = MappingCompleteness.Measure(artifact, conversion);
        output.WriteLine(measure.Describe());

        var descriptor = OrmConvertor.Factories.DescriptorFactory.Create(target);
        var carried = conversion.Facts
            .Where(fact => fact.Outcome == FactOutcome.Carried && descriptor.SupportOf(fact.Category) != FactSupport.NotExpressible)
            .ToList();
        Assert.Equal(carried.Count, measure.Carried);

        var readBack = MappingCompleteness.ReadBack(artifact);
        var notReadBack = carried
            .Where(fact => !(readBack.TryGetValue(new FactSubject(fact.Entity, fact.Property, fact.Category), out var value) && MappingCompleteness.SameValue(fact.Category, fact.Value, value)))
            .Select(fact => new FactSubject(fact.Entity, fact.Property, fact.Category).ToString())
            .Order(StringComparer.Ordinal)
            .ToList();

        var recorded = RecordedDeviations.GetValueOrDefault((source, target), []).Order(StringComparer.Ordinal).ToList();
        Assert.True(
            notReadBack.SequenceEqual(recorded),
            $"{source} -> {target}: the facts the ledger carries and the parser of the target does not read back are not the recorded ones.{Environment.NewLine}"
            + $"not read back: {string.Join("; ", notReadBack.DefaultIfEmpty("-"))}{Environment.NewLine}"
            + $"recorded:      {string.Join("; ", recorded.DefaultIfEmpty("-"))}{Environment.NewLine}"
            + measure.Detail);
    }

    /// <summary>A target that expresses no fact of the reference gets no share, not a share of zero.</summary>
    [Fact]
    public void ATargetThatExpressesNothingHasNoShare()
    {
        var conversion = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, CrossFrameworkInputs.Units(ORMEnum.EFCore));
        var measure = MappingCompleteness.Measure(Artifact(conversion, ORMEnum.EFCore, ORMEnum.Dapper), conversion);

        output.WriteLine(measure.Describe());
        Assert.True(measure.Expressible <= measure.Carried + conversion.Facts.Count(fact => fact.Outcome == FactOutcome.Dropped));
        if (measure.Expressible == 0)
        {
            Assert.Null(measure.Share);
        }
    }

    /// <summary>The negative half: a table renamed in the artifact is a fact not transferred, and the detail names it with both values.</summary>
    [Fact]
    public void ARenamedTableIsAFactNotTransferred()
    {
        var conversion = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, CrossFrameworkInputs.Units(ORMEnum.EFCore));
        var whole = MappingCompleteness.Measure(Artifact(conversion, ORMEnum.EFCore, ORMEnum.NHibernate), conversion);

        var mapping = conversion.Sources.Single(source => source.ContentType == ConversionContentType.XML);
        var renamed = mapping.Content.Replace("table=\"Customers\"", "table=\"CustomersX\"", StringComparison.Ordinal);
        Assert.NotEqual(mapping.Content, renamed);

        var mutated = Artifact(conversion, ORMEnum.EFCore, ORMEnum.NHibernate, source => source == mapping ? renamed : source.Content);
        var measure = MappingCompleteness.Measure(mutated, conversion);
        output.WriteLine(measure.Describe());

        Assert.Equal(whole.ReadBack - 1, measure.ReadBack);
        Assert.Contains("Customer TableName: expected 'Customers', read 'CustomersX'", measure.Detail);
    }

    private static ForeignArtifact Artifact(ConversionResult conversion, ORMEnum source, ORMEnum target, Func<ConversionSource, string>? content = null)
        => new(
            $"cross-{source}-{target}",
            ".",
            new ForeignKey(ForeignKey.Cross, source.ToString()),
            source,
            target,
            ExperimentRun.Producer,
            [.. conversion.Sources.Select((artifact, index) => new ForeignUnit(
                artifact.Name ?? $"{artifact.ContentType}-{index + 1}{ForeignInputs.Extension(artifact.ContentType)}",
                artifact.ContentType,
                content?.Invoke(artifact) ?? artifact.Content))],
            "{}");
}
