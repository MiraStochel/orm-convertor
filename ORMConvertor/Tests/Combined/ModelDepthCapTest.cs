using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Experiment;

namespace Tests.Combined;

/// <summary>
/// The cap of decision 191 over the chains the text writes without a bracket: a sum, a CASE in
/// the ELSE of a CASE, a run of NOTs, a set operation after a set operation. Measured with the
/// cap off, every one of them ended the process between 512 and 2048 links - in ScriptDom's
/// grammar, in a reader of ours or in a walk after the reading -, so the suite stays where the
/// readers were measured to survive: one link past the cap, and 1024 links, where the counts and
/// the guard have to stop the chain before the descent that would end the run.
/// </summary>
public class ModelDepthCapTest
{
    private const int Cap = ParseLimits.DefaultMaxNestingDepth;

    private static readonly ORMEnum[] Sources = [ORMEnum.Dapper, ORMEnum.EFCore, ORMEnum.NHibernate, ORMEnum.Hibernate, ORMEnum.EclipseLink, ORMEnum.MyBatis];

    public static TheoryData<string, ORMEnum> Chains()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var axis in new[] { "expression", "case", "not", "unions" })
        {
            foreach (var source in LimitSeries.Axis(axis)!.Sources)
            {
                data.Add(axis, source);
            }
        }

        return data;
    }

    /// <summary>The chains whose depth only the model counts: in no language does a bracket or a keyword pair stand for one of their links.</summary>
    public static TheoryData<string, ORMEnum> ModelOnlyChains()
    {
        var data = new TheoryData<string, ORMEnum>();
        foreach (var axis in new[] { "expression", "unions" })
        {
            foreach (var source in LimitSeries.Axis(axis)!.Sources)
            {
                data.Add(axis, source);
            }
        }

        return data;
    }

    private static List<ConversionRecord> Records(string axis, ORMEnum source, int links)
        => ConversionHandler.Convert(
            source,
            source == ORMEnum.Dapper ? ORMEnum.EFCore : ORMEnum.Dapper,
            LimitSeries.Axis(axis)!.Units(source, links),
            catalogConnectionString: null).Records;

    private static ConversionRecord? DepthRefusal(IEnumerable<ConversionRecord> records)
        => records.FirstOrDefault(r => r.Kind == ConversionRecordKind.Failure
                                       && (r.Reason?.Contains($"than the {Cap} levels this instance reads", StringComparison.Ordinal) == true
                                           || r.Reason == ReaderStack.Reason));

    [Theory]
    [MemberData(nameof(ModelOnlyChains))]
    public void AChainAsDeepAsTheCapIsRead(string axis, ORMEnum source)
        => Assert.Null(DepthRefusal(Records(axis, source, Cap)));

    [Theory]
    [MemberData(nameof(ModelOnlyChains))]
    public void OneLinkPastTheCapIsRefusedWithTheDepth(string axis, ORMEnum source)
    {
        var refusal = DepthRefusal(Records(axis, source, Cap + 1));

        Assert.NotNull(refusal);
        Assert.Contains($"nests {Cap + 1} operations", refusal!.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A chain of 1024 links, where with the cap off the process ended: in ScriptDom's grammar
    /// for a CASE from T-SQL, in the LINQ reader for a sum from EF Core, in the HQL and JPQL
    /// parsers for a CASE, in the walks after the reading for the rest. With the cap on it is a
    /// refusal, and this test running to its end is the proof that nothing descended. Set
    /// operations stop at 256, the deepest chain of them every reader was measured to survive.
    /// </summary>
    [Theory]
    [MemberData(nameof(Chains))]
    public void AChainWhereTheProcessOnceEndedIsRefused(string axis, ORMEnum source)
    {
        var records = Records(axis, source, axis == "unions" ? 256 : 1024);

        // The LINQ reader reads a chain of conditionals as one CASE of as many branches, so
        // the model stays flat and the chain is translated; that the call returned is the test.
        if (axis == "case" && source == ORMEnum.EFCore)
        {
            Assert.Null(DepthRefusal(records));
            return;
        }

        Assert.NotNull(DepthRefusal(records));
    }

    /// <summary>A CASE nested as deep as the cap in a query language is read: CASE up to its END counts as one level, as a bracket does.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.Hibernate)]
    public void ACaseAsDeepAsTheCapIsRead(ORMEnum source)
        => Assert.Null(DepthRefusal(Records("case", source, Cap)));

    /// <summary>One CASE past the cap is refused before the grammar sees it, with the position of the CASE that crossed it.</summary>
    [Theory]
    [InlineData(ORMEnum.Dapper)]
    [InlineData(ORMEnum.MyBatis)]
    [InlineData(ORMEnum.NHibernate)]
    [InlineData(ORMEnum.Hibernate)]
    [InlineData(ORMEnum.EclipseLink)]
    public void OneCasePastTheCapIsRefusedWithAPosition(ORMEnum source)
    {
        var refusal = DepthRefusal(Records("case", source, Cap + 1));

        Assert.NotNull(refusal);
        Assert.Contains("line ", refusal!.Reason, StringComparison.Ordinal);
        Assert.Contains("column ", refusal.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaseNestsOnlyInAQueryLanguage()
    {
        var limits = new ParseLimits { MaxNestingDepth = 3 };
        List<SourceToken> cases = [.. Enumerable.Range(1, 4).Select(i => new SourceToken("CASE", 1, i))];

        Assert.Equal(4, NestingDepthGuard.FirstBeyond(cases, limits, caseNests: true)?.Column);
        Assert.Null(NestingDepthGuard.FirstBeyond(cases, limits));
    }

    [Fact]
    public void ACaseClosedByItsEndAddsNothingAfterIt()
    {
        var limits = new ParseLimits { MaxNestingDepth = 3 };
        var tokens = Enumerable.Range(0, 100).SelectMany(_ => new[] { new SourceToken("case", 1, 1), new SourceToken("x", 1, 2), new SourceToken("end", 1, 3) });

        Assert.Null(NestingDepthGuard.FirstBeyond(tokens, limits, caseNests: true));
    }

    [Fact]
    public void ARunOfPrefixOperatorsNestsWhileItLasts()
    {
        var limits = new ParseLimits { MaxNestingDepth = 3 };
        List<SourceToken> run = [new("(", 1, 1), new("NOT", 1, 2), new("not", 1, 3), new("!", 1, 4)];
        var apart = Enumerable.Range(0, 100).SelectMany(_ => new[] { new SourceToken("NOT", 1, 1), new SourceToken("x", 1, 2) });

        Assert.Equal(4, NestingDepthGuard.FirstBeyond(run, limits)?.Column);
        Assert.Null(NestingDepthGuard.FirstBeyond(apart, limits));
    }

    [Fact]
    public void AnOperatorWhoSwitchesTheCapOffSwitchesTheDepthOffToo()
    {
        var records = ConversionHandler.Convert(
            ORMEnum.NHibernate,
            ORMEnum.Dapper,
            LimitSeries.Axis("expression")!.Units(ORMEnum.NHibernate, Cap + 1),
            catalogConnectionString: null,
            declaredSourceDialect: null,
            new ParseLimits { MaxNestingDepth = ParseLimits.Unlimited }).Records;

        Assert.Null(DepthRefusal(records));
    }
}
