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
/// except the stated limits of the measure recorded here by name, each with its reason, so that
/// a new one fails the test rather than lowering a share in silence. The negative half: an
/// artifact whose column was renamed is read back short of that fact, and the detail names it;
/// and the two readings by which a value read back states the reference's fact (decision 188)
/// hold where they should and nowhere else.
/// </summary>
public class MappingCompletenessTest(ITestOutputHelper output)
{
    /// <summary>
    /// Facts the ledger counts as carried and the read-back does not find, by direction, with
    /// why: the artifact does state the fact, but in a form the representation has no place
    /// for, so the parser of the target does not read it. Both are stated limits of the measure
    /// (decision 188): reading them would take a second reading of the target framework inside
    /// the measure, beside its parser. An entry that disappears fails the test as well.
    /// </summary>
    private static readonly Dictionary<(ORMEnum Source, ORMEnum Target), string[]> RecordedDeviations = new()
    {
        // MyBatis writes the key as the <id> of the result map, which its parser does not read
        // as a key: an <id> flags the identity of a result, which a key is only the obvious
        // choice for, and a MyBatis source states no key (decisions 063 and 084).
        [(ORMEnum.EFCore, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.NHibernate, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.Hibernate, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],
        [(ORMEnum.EclipseLink, ORMEnum.MyBatis)] = ["Customer.CustomerId PrimaryKey"],

        // JPA states the database type of a column through the Java type, and in a literal
        // columnDefinition only where the vocabulary needs it; the parsers read no database type
        // out of int or BigDecimal, and out of String only the unicode facet, with no family
        // (decision 067: the type a provider derives from the language type is not materialized).
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
            .Where(fact => !MappingCompleteness.Transferred(new FactSubject(fact.Entity, fact.Property, fact.Category), fact.Value, readBack))
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

    /// <summary>
    /// A strategy read back as Identity states the reference's Auto, the same column pinned
    /// (decision 188); nothing else states Auto, and Auto does not state Identity, whose pin it loses.
    /// </summary>
    [Theory]
    [InlineData("Auto", "Identity", true)]
    [InlineData("Identity", "Auto", false)]
    [InlineData("Auto", "Sequence", false)]
    [InlineData("Auto", "Assigned", false)]
    [InlineData("Identity", "Identity", true)]
    public void IdentityReadBackStatesAutoAndNotTheOtherWayRound(string expected, string read, bool transferred)
    {
        var subject = new FactSubject("Customer", "CustomerId", MappingFactCategory.PrimaryKeyStrategy);

        Assert.Equal(transferred, MappingCompleteness.Transferred(subject, expected, new Dictionary<FactSubject, string?> { [subject] = read }));
    }

    /// <summary>
    /// A part of the key read back states the NOT NULL of its column where the read-back says
    /// nothing of the nullability (decision 188); a property outside the key does not, a
    /// nullability the read-back states stays its statement, and the key states no NULL.
    /// </summary>
    [Theory]
    [InlineData("not null", true, null, true)]
    [InlineData("not null", false, null, false)]
    [InlineData("not null", true, "nullable", false)]
    [InlineData("nullable", true, null, false)]
    [InlineData("not null", false, "not null", true)]
    public void AKeyPartStatesTheNotNullOfItsColumn(string expected, bool keyPart, string? read, bool transferred)
    {
        var subject = new FactSubject("Customer", "CustomerId", MappingFactCategory.Nullability);

        var readBack = new Dictionary<FactSubject, string?>();
        if (keyPart)
        {
            readBack[subject with { Category = MappingFactCategory.PrimaryKey }] = null;
        }

        if (read is not null)
        {
            readBack[subject] = read;
        }

        Assert.Equal(transferred, MappingCompleteness.Transferred(subject, expected, readBack));
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
