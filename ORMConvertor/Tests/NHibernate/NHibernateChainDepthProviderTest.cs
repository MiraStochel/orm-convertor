using AbstractWrappers.Diagnostics;
using Model;
using NHibernateWrappers;
using OrmConvertor;
using Tests.Database;
using Tests.Experiment;
using Tests.Foreign;

namespace Tests.NHibernate;

/// <summary>
/// What NHibernate's own HQL parser reads (decision 192), measured where the descriptor relies
/// on it. Its recursion goes through every level of precedence for each link of a chain, and on
/// the 1.5 MB every default thread of .NET has on Windows it overflowed the stack - which ends
/// the consumer's process - from 104 links of a sum, 112 of NOTs and 120 of CASEs, while every
/// shape read 96. The descriptor holds HQL to a third of that and writes a deeper query in
/// native SQL. The overflow itself is never tried here, since it would end the run: only that
/// a chain as deep as the descriptor states is accepted in HQL, and that a deeper one goes out
/// in native SQL and is accepted too; both run, except a CASE past the ten levels SQL Server
/// nests. An upgrade that makes the parser's frames larger fails the first half.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class NHibernateChainDepthProviderTest(TestSchemaFixture fixture, ITestOutputHelper output)
{
    private static readonly int ChainDepth = NHibernateDescriptor.Instance.ChainDepth!.Value;

    public static TheoryData<string> Chains() => ["expression", "case", "not"];

    private (ForeignVerdict Verdict, ConversionResult? Conversion) Judge(string axisId, int links)
    {
        var axis = LimitSeries.Axis(axisId)!;
        var input = new ExperimentInput($"series-{axisId}-{links}-NHibernate", axis.Key(links), ORMEnum.NHibernate, axis.Units(ORMEnum.NHibernate, links));
        var judged = ExperimentRun.Judge(input, ORMEnum.NHibernate, fixture);
        output.WriteLine(judged.Verdict.Describe());
        return judged;
    }

    /// <summary>
    /// The level a chain of the shape reaches when NHibernate accepts it: the fourth, where the
    /// query runs - except a CASE, which SQL Server nests to ten levels (Msg 125) whatever wrote
    /// the query, so that past ten the server refuses what NHibernate accepted.
    /// </summary>
    private static int Reaches(string axis) => axis == "case" ? ForeignLevel.Accepted : ForeignLevel.Ran;

    [Theory]
    [MemberData(nameof(Chains))]
    public void AChainAsDeepAsTheDescriptorStatesIsAcceptedInHql(string axis)
    {
        fixture.SkipIfUnavailable();

        var (verdict, conversion) = Judge(axis, ChainDepth);

        Assert.Equal(ForeignArtifact.LanguageTarget, verdict.Language);
        Assert.DoesNotContain(conversion!.Records, record => record.Kind == ConversionRecordKind.Fallback);
        Assert.Equal(Reaches(axis), verdict.Reached);
    }

    [Theory]
    [MemberData(nameof(Chains))]
    public void ADeeperChainGoesOutInNativeSql(string axis)
    {
        fixture.SkipIfUnavailable();

        var (verdict, conversion) = Judge(axis, 2 * ChainDepth);

        Assert.Equal(ForeignArtifact.LanguageNativeSql, verdict.Language);
        Assert.Contains(conversion!.Records, record => record.Kind == ConversionRecordKind.Fallback && record.Reason!.Contains("is held to", StringComparison.Ordinal));
        Assert.Equal(Reaches(axis), verdict.Reached);
    }
}
