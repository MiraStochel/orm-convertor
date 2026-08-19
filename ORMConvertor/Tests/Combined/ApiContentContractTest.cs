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

    [Fact]
    public void UnitIdsAreUnique()
    {
        var ids = RequiredContent.GetRequiredContent.SelectMany(d => d.Required).Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    /// <summary>
    /// A query unit the source framework has no parser for would be a box the user fills and
    /// the tool then refuses - the interface must not ask for one.
    /// </summary>
    [Fact]
    public void EveryQueryUnitIsAskedInALanguageTheSourceCanRead()
    {
        foreach (var definition in RequiredContent.GetRequiredContent)
        {
            foreach (var unit in definition.Required.Where(c => c.ContentType.IsQuery()))
            {
                var sources = new List<ConversionSource>
                {
                    new() { Content = Samples.GetSamples[unit.Id], ContentType = unit.ContentType },
                };

                var result = OrmConvertor.ConversionHandler.Convert(definition.OrmType, ORMEnum.Dapper, sources);

                Assert.Contains(result.Sources, s => s.ContentType.IsQuery());
            }
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
