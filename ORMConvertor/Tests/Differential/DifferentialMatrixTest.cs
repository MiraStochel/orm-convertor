using AbstractWrappers.Descriptors;
using Model;
using Tests.Combined;

namespace Tests.Differential;

/// <summary>
/// What the matrix claims about itself (decision 089), after the shape decision 087 gave
/// the same question at F12: a number written into a document goes stale with the first
/// query anybody adds, and a number the suite checks against its own data cannot.
///
/// The criterion of F13 is split over two claims, because neither suite sees into the
/// other: the matrix states that there are at least thirty pairs, and each suite states
/// that it ran every pair the matrix assigns to it. Together they are the criterion, and
/// neither half can be met by leaving the other out.
/// </summary>
public class DifferentialMatrixTest
{
    [Fact]
    public void TheMatrixStatesAtLeastThePairsTheCriterionAsks()
    {
        var pairs = DifferentialMatrix.Pairs().Count();

        Assert.True(
            pairs >= DifferentialMatrix.RequiredPairs,
            $"F13 asks for at least {DifferentialMatrix.RequiredPairs} pairs of queries and the matrix "
            + $"states {pairs}. A query added to matrix.txt brings five of them per source, one per "
            + "framework other than that source.");
    }

    /// <summary>
    /// Every pair is run by one suite or the other. A framework that neither suite owns
    /// would leave pairs in the matrix that nobody ever ran, and the count above would then
    /// be a claim about a file rather than about a run.
    /// </summary>
    [Fact]
    public void EveryPairIsOwnedByASuite()
    {
        // Which suite a target belongs to is its ecosystem, and the ecosystem is the
        // descriptor's own word (decision 090) rather than a list written here that a
        // seventh framework would not be on.
        Assert.All(
            DifferentialMatrix.Pairs(),
            pair => Assert.True(
                DotNetQueryRunner.Owns(pair.Target)
                    || FrameworkDescriptors.EcosystemOf(pair.Target) == Ecosystem.Java,
                $"{pair.Query.Id}: no suite runs {pair.Target}, so this pair is in the matrix and in no run."));
    }

    /// <summary>
    /// Every framework is the source of some query. Without it the matrix could be thirty
    /// pairs that all translate out of one ecosystem, which would measure the easy half of
    /// what T2 asks for and call it the whole.
    /// </summary>
    [Fact]
    public void EveryFrameworkIsTheSourceOfAQuery()
    {
        var sources = DifferentialMatrix.Queries.SelectMany(query => query.Sources).Distinct().ToList();

        Assert.All(
            Enum.GetValues<ORMEnum>(),
            framework => Assert.Contains(framework, sources));
    }

    /// <summary>
    /// Every category of T2 the shared manifest states is a query of the matrix, so every
    /// category has the fourth level in every direction its sources and targets allow. This
    /// is the claim the row T2 of the traceability makes since the matrix grew over the
    /// categories: a category added to the manifest without a canonical result is a failure
    /// here, not a category measured at the first three levels and quietly not at the fourth.
    /// </summary>
    [Fact]
    public void EveryCategoryOfTheManifestIsAQueryOfTheMatrix()
    {
        var measured = DifferentialMatrix.Queries
            .Where(query => query.Category is not null)
            .Select(query => query.Category!)
            .ToList();

        Assert.All(
            QueryShapeInputs.Categories,
            category => Assert.True(
                measured.Contains(category.Name),
                $"categories.txt states the category {category.Name} and matrix.txt has no query for it, "
                + "so the category is measured at levels 1 to 3 and not at the fourth."));
    }

    /// <summary>
    /// Every query has a canonical result, and it is not empty. An empty file would make
    /// every direction agree with every other about nothing at all.
    /// </summary>
    [Fact]
    public void EveryQueryHasACanonicalResult()
    {
        Assert.All(DifferentialMatrix.Queries, query =>
        {
            var rows = query.CanonicalResult();

            Assert.True(rows.Count > 0, $"{query.Id}: the canonical result is empty.");
            Assert.All(rows, row => Assert.Equal(
                query.Fields.Count,
                row.Split(ResultRow.Separator).Length));
        });
    }

    /// <summary>
    /// The matrix says a refused direction in that word (decision 089): a target the
    /// manifest names as refusing a category is paired with none of its sources, and is
    /// listed among the refused directions instead, where a suite asserts the refusal. No
    /// category is refused since decision 113 turned the three refusals into fallbacks, so
    /// this holds over an empty list today and keeps the rule for the next refusal.
    /// </summary>
    [Fact]
    public void ARefusedTargetIsNoPairAndIsStatedAsRefused()
    {
        Assert.All(DifferentialMatrix.RefusedDirections(), direction => Assert.DoesNotContain(
            DifferentialMatrix.Pairs(),
            pair => pair.Query.Id == direction.Query.Id && pair.Source == direction.Source && pair.Target == direction.Target));
    }

    /// <summary>
    /// The escape path is measured at the fourth level (decision 113): a target the manifest
    /// names as falling back is a pair of every source of the category, so its artifact runs
    /// and is compared with the canonical result like any translation.
    /// </summary>
    [Fact]
    public void AFallbackTargetIsAPairOfEverySource()
    {
        var fallbacks = DifferentialMatrix.FallbackDirections().ToList();

        Assert.NotEmpty(fallbacks);
        Assert.All(fallbacks, direction => Assert.Contains(
            DifferentialMatrix.Pairs(),
            pair => pair.Query.Id == direction.Query.Id && pair.Source == direction.Source && pair.Target == direction.Target));
    }
}
