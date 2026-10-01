using AbstractWrappers.Diagnostics;
using Model;
using ORMConvertorAPI.Data;

namespace Tests.Combined;

/// <summary>
/// The interface contract: every unit the API asks a user to fill has a sample to fill it
/// with, and every language it asks for is one the source framework can actually read
/// (decision 025). Nothing checked this before, which is how three sample ids came to point
/// at one EF Core query while one of them had no unit at all.
/// </summary>
public class ApiContentContractTest
{
    [Fact]
    public void EveryRequiredUnitHasASample()
    {
        var samples = Samples.GetSamples;

        foreach (var definition in RequiredContent.GetRequiredContent)
        {
            foreach (var unit in definition.Required)
            {
                Assert.True(
                    samples.ContainsKey(unit.Id),
                    $"{definition.OrmType} asks for unit {unit.Id} ({unit.Description}) and no sample fills it.");
            }
        }
    }

    [Fact]
    public void EverySampleFillsARequiredUnit()
    {
        var required = RequiredContent.GetRequiredContent
            .SelectMany(d => d.Required)
            .Select(c => c.Id)
            .ToHashSet();

        foreach (var id in Samples.GetSamples.Keys)
        {
            Assert.True(required.Contains(id), $"Sample {id} fills no unit the interface asks for.");
        }
    }

    /// <summary>
    /// A framework the interface never asks about is a framework nobody can convert from,
    /// whatever the wrapper behind it can do: the screen is built from this list alone
    /// (decision 025), so a value of the enum missing here is a wrapper that shipped without
    /// a way in. The cross-framework matrices take their directions from the enum for the
    /// same reason; this is the same guard on the interface's side.
    /// </summary>
    [Fact]
    public void EveryFrameworkHasARowOfItsOwn()
    {
        var declared = RequiredContent.GetRequiredContent.Select(d => d.OrmType).ToList();

        Assert.Equal(Enum.GetValues<ORMEnum>().OrderBy(f => f), declared.OrderBy(f => f));
    }

    /// <summary>
    /// Each row offers languages, each once (decision 111): a unit declares the language of
    /// its file and the source framework reads the roles out of it, so a value that names a
    /// role - an artifact's - is never asked for, and a language asked for twice would be a
    /// role in disguise.
    /// </summary>
    [Fact]
    public void EveryFrameworkOffersEachOfItsLanguagesOnce()
    {
        foreach (var definition in RequiredContent.GetRequiredContent)
        {
            var offered = definition.Required.Select(u => u.ContentType).ToList();

            Assert.All(offered, type => Assert.Equal(type, type.LanguageOf()));
            Assert.Equal(offered.Count, offered.Distinct().Count());
        }
    }

    /// <summary>
    /// And each row reaches both halves of a conversion. A framework whose sample set yielded
    /// only a mapping would leave its query branch unreachable from the interface even where
    /// its parser reads one; since decision 111 the halves are found inside the units rather
    /// than asked for one by one, so it is the output that has to carry both.
    /// </summary>
    [Fact]
    public void EveryFrameworksSampleSetYieldsAMappingAndAQuery()
    {
        foreach (var definition in RequiredContent.GetRequiredContent)
        {
            var sources = definition.Required
                .Select(c => new ConversionSource { Content = Samples.GetSamples[c.Id], ContentType = c.ContentType })
                .ToList();

            var result = OrmConvertor.ConversionHandler.Convert(definition.OrmType, ORMEnum.Dapper, sources);

            Assert.Contains(result.Sources, s => !s.ContentType.IsQuery());
            Assert.Contains(result.Sources, s => s.ContentType.IsQuery());
        }
    }

    [Fact]
    public void UnitIdsAreUnique()
    {
        var ids = RequiredContent.GetRequiredContent.SelectMany(d => d.Required).Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// A language the source framework has no parser for would be a box the user fills and
    /// the tool then refuses - the interface must not ask for one; and a sample that yields
    /// nothing would be a box the tool reads and throws away.
    ///
    /// The units are converted together, because that is what the interface asks for and
    /// what the user fills: a query whose filter carries a parameter takes its scalar from the
    /// mapping IR (decision 083), so a unit on its own is not the question the interface poses.
    /// </summary>
    [Fact]
    public void EveryLanguageAskedForIsReadAndYields()
    {
        foreach (var definition in RequiredContent.GetRequiredContent)
        {
            var sources = definition.Required
                .Select(c => new ConversionSource { Content = Samples.GetSamples[c.Id], ContentType = c.ContentType, Name = c.Description })
                .ToList();

            var result = OrmConvertor.ConversionHandler.Convert(definition.OrmType, ORMEnum.Dapper, sources);

            Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure && r.Unit is not null);
        }
    }
}

/// <summary>
/// The orchestration refuses a framework it does not know, on both sides. The source side
/// used to return no parsers at all, which came back as an empty result and no error.
/// </summary>
public class UnsupportedFrameworkTest
{
    [Fact]
    public void AnUnsupportedTargetIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OrmConvertor.ConversionHandler.Convert(ORMEnum.Dapper, (ORMEnum)99, []));
    }

    [Fact]
    public void AnUnsupportedSourceIsRefused()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OrmConvertor.ConversionHandler.Convert((ORMEnum)99, ORMEnum.Dapper, []));
    }
}
