using Model;
using Tests.Database;

namespace Tests.Differential;

/// <summary>
/// The negative half of F13: a deliberately wrong translation has to be detected
/// (decision 089). Every mutation the artifact can carry is applied to it, the mutated
/// artifact is run against the same fixture, and its result has to differ from the
/// canonical one.
///
/// Without this the whole matrix could be green for the wrong reason - a comparison that
/// always says yes says nothing - and two of the mutations test the comparison rather than
/// the translation: dropping an ordering is caught only by a comparison that takes order
/// seriously where the query gives one, and swapping two projected fields only by one that
/// pairs values with fields rather than counting them.
/// </summary>
[Collection(TestSchemaCollection.Name)]
public class DifferentialMutationTest(TestSchemaFixture fixture)
{
    /// <summary>
    /// Every (query, target, mutation) this suite runs. Which mutations a query carries is
    /// the matrix's statement - a query with no ordering cannot lose one - and it is stated
    /// rather than discovered, so nothing here can quietly stop running.
    /// </summary>
    public static TheoryData<string, ORMEnum, string> Mutations()
    {
        var data = new TheoryData<string, ORMEnum, string>();

        foreach (var (query, target) in DifferentialMatrix.Pairs().Where(pair => DotNetQueryRunner.Owns(pair.Target)))
        {
            foreach (var mutation in query.Mutations)
            {
                data.Add(query.Id, target, mutation);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public void AMutatedTranslationDoesNotMatchTheCanonicalResult(string id, ORMEnum target, string mutationKey)
    {
        fixture.SkipIfUnavailable();

        var query = DifferentialVerificationTest.Query(id);
        var conversion = DotNetQueryRunner.Translate(query, target, fixture);

        var artifact = conversion.Sources
            .Single(source => source.ContentType == ConversionContentType.CSharpQuery)
            .Content;

        var mutation = DifferentialMutation.Of(mutationKey);
        var mutated = mutation.Apply(artifact);

        // Which mutations a query carries is stated in the matrix rather than discovered
        // here, so that one which stops reaching the artifact of some target is a failure
        // instead of a case that quietly stops running.
        Assert.True(
            mutated != artifact,
            $"{id} -> {target}: the matrix says this query carries \"{mutationKey}\", and the rule "
            + $"changed nothing in the artifact:{Environment.NewLine}{artifact}");

        // A mutation is detected the moment the artifact stops answering with the canonical
        // result, and there are two ways for that to happen. It can run and return other
        // rows, which is the case the comparison is really about; or it can fail to compile,
        // to bind or to materialize, because a wrong query is often also an unusable one -
        // a projection read in the wrong order hands Dapper a name where it wants a number,
        // and a filter dropped from HQL leaves a parameter nobody binds. Both are the wrong
        // translation being caught; only silence would not be.
        List<string>? rows = null;

        try
        {
            rows = DotNetQueryRunner.Run(query, target, fixture, mutated);
        }
        catch (Exception)
        {
            // Detected the loud way: the artifact never got as far as claiming the
            // canonical result. The assertion below is deliberately outside this block, so
            // that a failing assertion is never mistaken for a detection.
        }

        if (rows is not null)
        {
            Assert.False(
                query.CanonicalResult().SequenceEqual(rows),
                $"{id} -> {target}: {mutation.Name}, and the result was the canonical one all the same. "
                + "Either the fixture does not separate the two, or the comparison does not look at "
                + "what the mutation changed.");
        }
    }
}
