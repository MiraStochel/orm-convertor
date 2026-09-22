using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using ORMConvertorAPI.Data;
using ORMConvertorAPI.Dtos;

namespace Tests.Combined;

/// <summary>
/// The examples of the explanatory page (decision 099). Which examples the page shows is its
/// content, not a decision; what the decision fixes is a floor - at least seven examples,
/// every boundary between the ecosystems crossed, every framework taking part - and this
/// class holds it. The page itself claims no requirement and has no tests (decision 032), so
/// what is asserted here is the data the server hands it: every example is a conversion the
/// tool actually performs, in languages its source reads. Without this, a parser change that
/// broke an example would surface on the page rather than in the suite - the live example
/// cannot drift from the tool in what it shows, but it can stop working.
/// </summary>
public class ExampleCatalogTest
{
    public static TheoryData<string> Keys()
    {
        var data = new TheoryData<string>();
        foreach (var example in Examples.GetExamples)
        {
            data.Add(example.Key);
        }

        return data;
    }

    private static ExampleDefinition Example(string key) => Examples.GetExamples.Single(e => e.Key == key);

    /// <summary>
    /// The example converts without a Failure and yields both halves of a conversion. Losses,
    /// conventions and incompleteness are what the records band is there to explain; a
    /// Failure would be an example of the tool refusing its own example. No catalog is
    /// configured, as in the contract tests (decision 043), so the records are the ones a
    /// bare instance shows.
    /// </summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryExampleConvertsWithoutAFailure(string key)
    {
        var example = Example(key);

        var result = ConversionHandler.Convert(example.SourceOrm, example.TargetOrm, example.Units);

        var failures = result.Records.Where(r => r.Kind == ConversionRecordKind.Failure).ToList();
        Assert.True(
            failures.Count == 0,
            $"Example '{key}' fails:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures.Select(f => $"{f.Entity} {f.Unit} {f.Query}: {f.Reason}")));
        Assert.Contains(result.Sources, s => !s.ContentType.IsQuery());
        Assert.Contains(result.Sources, s => s.ContentType.IsQuery());
    }

    /// <summary>
    /// Every unit is a named file in a language its source framework reads - the same list the
    /// translation screen offers (decision 025) - so the page can send it as it is and the
    /// records point at a file the reader can see (decision 066).
    /// </summary>
    [Theory]
    [MemberData(nameof(Keys))]
    public void EveryUnitIsANamedFileTheSourceReads(string key)
    {
        var example = Example(key);
        var read = RequiredContent.GetRequiredContent
            .Single(definition => definition.OrmType == example.SourceOrm)
            .Required
            .Select(unit => unit.ContentType)
            .ToHashSet();

        Assert.NotEmpty(example.Units);
        foreach (var unit in example.Units)
        {
            Assert.False(string.IsNullOrWhiteSpace(unit.Name), $"Example '{key}' has a unit without a name.");
            Assert.False(string.IsNullOrWhiteSpace(unit.Content), $"Unit '{unit.Name}' of example '{key}' is empty.");
            Assert.True(
                read.Contains(unit.ContentType),
                $"Unit '{unit.Name}' of example '{key}' is {unit.ContentType}, which {example.SourceOrm} does not read.");
        }

        Assert.Equal(example.Units.Count, example.Units.Select(unit => unit.Name).Distinct().Count());
    }

    /// <summary>The key is the anchor of the page's section, so it is unique and needs no escaping.</summary>
    [Fact]
    public void KeysAreUniqueAnchors()
    {
        var keys = Examples.GetExamples.Select(example => example.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", key));
    }

    /// <summary>
    /// The floor of decision 099: the page may grow, but it does not quietly shrink. Seven is
    /// where it started, not a quota - adding an example past it needs no decision.
    /// </summary>
    [Fact]
    public void TheSetHasAtLeastSevenExamples()
    {
        Assert.True(
            Examples.GetExamples.Count >= 7,
            $"The explanatory page shows {Examples.GetExamples.Count} examples; decision 099 keeps at least seven.");
    }

    /// <summary>
    /// The set crosses every boundary decision 099 names: inside each ecosystem and across
    /// them in both directions. The ecosystems are the ones the descriptors declare (decision
    /// 090), not a list written here.
    /// </summary>
    [Fact]
    public void TheSetCrossesEveryBoundary()
    {
        var crossings = Examples.GetExamples
            .Select(example => (FrameworkDescriptors.EcosystemOf(example.SourceOrm), FrameworkDescriptors.EcosystemOf(example.TargetOrm)))
            .ToHashSet();

        foreach (var source in Enum.GetValues<Ecosystem>())
        {
            foreach (var target in Enum.GetValues<Ecosystem>())
            {
                Assert.True(crossings.Contains((source, target)), $"No example translates from {source} to {target}.");
            }
        }
    }

    /// <summary>
    /// And every framework takes part, as a source or as a target. A framework the page never
    /// shows is one its reader never learns the tool translates; the guard is the one
    /// <see cref="ApiContentContractTest"/> keeps for the translation screen.
    /// </summary>
    [Fact]
    public void EveryFrameworkTakesPart()
    {
        var shown = Examples.GetExamples
            .SelectMany(example => new[] { example.SourceOrm, example.TargetOrm })
            .ToHashSet();

        foreach (var framework in Enum.GetValues<ORMEnum>())
        {
            Assert.True(shown.Contains(framework), $"No example translates from or to {framework}.");
        }
    }
}
