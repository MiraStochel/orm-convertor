using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;

namespace Tests.Combined;

/// <summary>
/// The query matrix through the real orchestration, in every direction <see cref="ORMEnum"/>
/// yields (<see cref="CrossFrameworkInputs"/>). Until now nothing exercised
/// <see cref="ConversionHandler"/>'s query path at all, so all but one direction could drop
/// their input in silence without a single test noticing.
/// </summary>
public class QueryMatrixTest
{
    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void EveryDirectionProducesAQueryArtifact(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        var query = result.Sources.Where(s => s.ContentType.IsQuery()).ToList();

        Assert.NotEmpty(query);
        Assert.All(query, artifact => Assert.False(string.IsNullOrWhiteSpace(artifact.Content)));
    }

    [Theory]
    [MemberData(nameof(CrossFrameworkInputs.Directions), MemberType = typeof(CrossFrameworkInputs))]
    public void NoDirectionDropsTheQueryInSilence(ORMEnum source, ORMEnum target)
    {
        var result = ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

        // Whatever else happens, the two Failure records the orchestration used to leave
        // unsaid - no query builder, no query parser - must never be the outcome now that
        // every direction is covered (decision 022).
        Assert.DoesNotContain(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("has no query"));
    }

    /// <summary>
    /// A row per framework, written by hand: the hallmark is a claim about one framework's
    /// output, not a direction, so a new framework adds its row here the way it adds one to
    /// <see cref="EnforcedMembersTest"/> (decision 037).
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, "SELECT")]
    [InlineData(ORMEnum.EFCore, "ctx.Set<")]
    [InlineData(ORMEnum.NHibernate, "from ")]
    public void EachTargetEmitsItsOwnQueryLanguage(ORMEnum target, string hallmark)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, CrossFrameworkInputs.Units(ORMEnum.EFCore));

        var query = result.Sources.First(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(hallmark, query);
    }

    /// <summary>
    /// Decision 025: a language whose target form is a string is emitted bare as well, so no
    /// consumer has to extract it from the surrounding C#.
    /// </summary>
    [Theory]
    [InlineData(ORMEnum.Dapper, ConversionContentType.SqlQuery)]
    [InlineData(ORMEnum.NHibernate, ConversionContentType.HqlQuery)]
    public void StringLanguagesAreAlsoEmittedBare(ORMEnum target, ConversionContentType expected)
    {
        var result = ConversionHandler.Convert(ORMEnum.EFCore, target, CrossFrameworkInputs.Units(ORMEnum.EFCore));

        Assert.Contains(result.Sources, s => s.ContentType == expected);
    }

    /// <summary>A blank query box is not a claim, so it produces neither artifact nor record.</summary>
    [Fact]
    public void ABlankQuerySourceIsNotAFailure()
    {
        List<ConversionSource> sources =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.EFCore),
            new() { Content = "   ", ContentType = ConversionContentType.CSharpQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, sources);

        Assert.DoesNotContain(result.Sources, s => s.ContentType.IsQuery());
        Assert.DoesNotContain(result.Records, r => r.Kind == ConversionRecordKind.Failure);
    }

    /// <summary>
    /// A set operation through the real orchestration: the parser reads the UNION, the
    /// entity maps travel to the target builder, and the LINQ target composes with Union.
    /// </summary>
    [Fact]
    public void AUnionTravelsThroughTheOrchestration()
    {
        const string unionQuery = """
            SELECT c.CustomerName FROM Sales.Customers AS c
            UNION
            SELECT c.CustomerName FROM Sales.Customers AS c
            """;

        List<ConversionSource> sources =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.Dapper),
            new() { Content = unionQuery, ContentType = ConversionContentType.SqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.Dapper, ORMEnum.EFCore, sources);

        var query = result.Sources.Single(s => s.ContentType == ConversionContentType.CSharpQuery).Content;

        Assert.Contains(".Union(", query);
        Assert.Contains("ctx.Set<Customer>()", query);
    }

    /// <summary>
    /// The source framework has no parser for this language, and that has to be said out loud
    /// - it was the silent `continue` decision 022 removed.
    /// </summary>
    [Fact]
    public void AQueryLanguageTheSourceCannotReadIsReported()
    {
        List<ConversionSource> sources =
        [
            .. CrossFrameworkInputs.MappingUnits(ORMEnum.EFCore),
            new() { Content = "from Customer c", ContentType = ConversionContentType.HqlQuery },
        ];

        var result = ConversionHandler.Convert(ORMEnum.EFCore, ORMEnum.Dapper, sources);

        Assert.Contains(
            result.Records,
            r => r.Kind == ConversionRecordKind.Failure && r.Reason.Contains("HqlQuery"));
    }
}
