using DapperWrappers;
using DatabaseCatalog;
using EFCoreWrappers;
using Model;
using OrmConvertor;
using SampleData;

namespace Tests.Combined;

/// <summary>
/// The run record of S6: every conversion carries its own identifier and the framework
/// versions it ran against, and the versions come from the descriptors (decision 013),
/// so the record cannot claim anything the parser or the generator did not assume.
/// </summary>
public class RunRecordTest
{
    private static List<ConversionSource> Sources() =>
    [
        new()
        {
            ContentType = ConversionContentType.CSharpEntity,
            Content = "public class Customer { public int Id { get; set; } }",
        },
    ];

    [Fact]
    public void TheRunRecordCarriesTheVersionsOfBothDescriptors()
    {
        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());

        Assert.NotEqual(Guid.Empty, result.RunId);
        Assert.Equal(ORMEnum.Dapper, result.SourceFramework);
        Assert.Equal(DapperDescriptor.Instance.Version, result.SourceFrameworkVersion);
        Assert.Equal(ORMEnum.EFCore, result.TargetFramework);
        Assert.Equal(EFCoreDescriptor.Instance.Version, result.TargetFrameworkVersion);

        // No connection string was passed, so the record must say the catalog took no
        // part - a first-class field beside the records (decision 030).
        Assert.Equal(CatalogConnectionState.NotConfigured, result.CatalogState);
    }

    [Fact]
    public void TheRunRecordCarriesTheVersionOfTheToolItself()
    {
        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());

        // S6 asks the record to carry versions, and S2 makes determinism conditional on
        // "the same version of the tool"; both are empty words unless the tool names its
        // own version next to the two framework versions. The number is set once for the
        // whole solution in Directory.Build.props (decision 034) and read back from the
        // assembly, so the assertion is that the build and the record agree - not a second
        // place where the number is written down.
        Assert.Equal(ToolRelease.Version, result.ToolVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.ToolVersion));
        Assert.DoesNotContain('+', result.ToolVersion);
    }

    [Fact]
    public void EveryRunGetsItsOwnIdentifier()
    {
        var first = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());
        var second = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, Sources());

        Assert.NotEqual(first.RunId, second.RunId);
    }

    /// <summary>
    /// A richer input than <see cref="Sources"/>: two related entities and a query, so that
    /// the determinism claim is made over a run with several entities, a relation to
    /// resolve and records to emit. One entity would pass while any order-dependent step
    /// over a set of entities stayed broken.
    /// </summary>
    private static List<ConversionSource> RelatedSources() =>
    [
        new()
        {
            ContentType = ConversionContentType.CSharpEntity,
            Content = CustomerSampleEFCore.Entity,
        },
        new()
        {
            ContentType = ConversionContentType.CSharpQuery,
            Content = CustomerSampleEFCore.Query,
        },
    ];

    [Fact]
    public void RepeatingTheSameRunProducesTheSameArtifactsAndRecords()
    {
        // S2 asks for byte-wise identical artifacts across repeated runs over the same
        // input, configuration and tool version. The identifier is the one field allowed
        // to differ (see above); everything a caller would act on must not.
        var first = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, RelatedSources());
        var second = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.NHibernate, RelatedSources());

        // Guard the guard: an empty result would satisfy every equality below.
        Assert.NotEmpty(first.Sources);

        Assert.Equal(
            first.Sources.Select(s => (s.ContentType, s.Content)),
            second.Sources.Select(s => (s.ContentType, s.Content)));

        // The records are part of the answer, not commentary on it (decision 010), so
        // their order and content are as much a determinism claim as the artifacts are.
        Assert.Equal(
            first.Records.Select(r => (r.Kind, r.Entity, r.Property, r.Artifact, r.Category, r.Feature, r.Reason)),
            second.Records.Select(r => (r.Kind, r.Entity, r.Property, r.Artifact, r.Category, r.Feature, r.Reason)));

        Assert.Equal(first.ToolVersion, second.ToolVersion);
        Assert.Equal(first.SourceFrameworkVersion, second.SourceFrameworkVersion);
        Assert.Equal(first.TargetFrameworkVersion, second.TargetFrameworkVersion);
    }

    /// <summary>
    /// S2 is a claim about the tool, not about one direction, so the claim is made in every
    /// direction the enum yields (<see cref="CrossFrameworkInputs"/>). The test above holds
    /// one pair in detail; this one holds the whole matrix, so a step that happened to be
    /// order-dependent in a wrapper nobody repeated the claim for cannot hide.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void EveryDirectionRepeatsItself(ORMEnum source, ORMEnum target)
    {
        var first = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));
        var second = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        Assert.Equal(
            first.Sources.Select(s => (s.ContentType, s.Content)),
            second.Sources.Select(s => (s.ContentType, s.Content)));

        Assert.Equal(
            first.Records.Select(r => (r.Kind, r.Entity, r.Property, r.Artifact, r.Category, r.Feature, r.Unit, r.Query, r.Reason)),
            second.Records.Select(r => (r.Kind, r.Entity, r.Property, r.Artifact, r.Category, r.Feature, r.Unit, r.Query, r.Reason)));
    }

    /// <summary>
    /// The other half of the same claim: the input is a set of units, not a sequence, wherever
    /// the source framework does not itself order its artifacts (decision 068). NHibernate
    /// states that order, so this asks it of the pair whose order the reader must not depend
    /// on - the class and the mapping - and expects the same artifacts either way round.
    /// </summary>
    [Fact]
    public void TheOrderOfTheInputUnitsDoesNotDecideTheOutput()
    {
        var units = CrossFrameworkInputs.MappingUnits(ORMEnum.NHibernate);
        var reversed = Enumerable.Reverse(units).ToList();

        var forward = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore, units);
        var backward = ConversionHandler.Convert(ORMEnum.NHibernate, ORMEnum.EFCore, reversed);

        Assert.NotEmpty(forward.Sources);
        Assert.Equal(
            forward.Sources.Select(s => (s.ContentType, s.Content)),
            backward.Sources.Select(s => (s.ContentType, s.Content)));
    }
}
