using AbstractWrappers.Diagnostics;
using Model;
using OrmConvertor;
using Tests.Differential;

namespace Tests.Combined;

/// <summary>
/// What the matrix claims about itself at F10 (decision 090), in the shape decision 087
/// gave the same question at F12 and decision 089 at F13: the criterion counts, so the
/// suite counts, and no document holds a number that the next direction would age.
///
/// The criterion has two sentences and they ask different things. **One end-to-end
/// scenario per pair of ecosystems** is the fourth level of decision 016 carried across
/// the boundary - a translation whose artifact really ran and whose rows were judged -
/// and the differential matrix (decision 089) is where those live. **At least thirty
/// cross-ecosystem translations** is the wider count, and a translation is counted once
/// per scenario and direction, whatever level judged it.
/// </summary>
public class CrossEcosystemMatrixTest
{
    /// <summary>What the criterion of F10 asks of the matrix, held here rather than in a document.</summary>
    private const int RequiredTranslations = 30;

    /// <summary>
    /// Nothing crosses the boundary in silence: either both halves of F10's sentence come
    /// out - the entity side and the query side - or the conversion says why not. The
    /// three directions that take the second branch are the ones where the source states
    /// no primary key and the target requires one (Dapper and MyBatis into NHibernate and
    /// the two JPA implementations), and that is F6's subject rather than a gap in F10:
    /// with a catalog connected they complete, which is what the differential matrix runs.
    /// </summary>
    [Theory]
    [MemberData(nameof(CrossDirections))]
    public void EveryCrossEcosystemDirectionArrivesWholeOrSaysWhyNot(ORMEnum source, ORMEnum target)
    {
        var result = Translate(source, target);

        Assert.Contains(result.Sources, s => s.ContentType.IsQuery());

        if (!result.Sources.Any(s => !s.ContentType.IsQuery()))
        {
            Assert.Contains(result.Records, r => r.Kind == ConversionRecordKind.Failure);
        }
    }

    /// <summary>
    /// The count itself. It is a sum over the two matrices the repository has, and not a
    /// third place stating the same thing: the sample scenarios, whose translation this
    /// test performs, and the differential queries, whose translation is performed and run
    /// by whichever suite owns the target (decision 089). The halves cannot be merged,
    /// because the differential scenarios with a keyless source need a catalog to arrive
    /// whole and this test runs dry - so each is counted where its own guarantee lives.
    /// </summary>
    [Fact]
    public void TheMatrixStatesAtLeastTheTranslationsTheCriterionAsks()
    {
        var sample = WholeSampleTranslations();
        var differential = DifferentialCrossEcosystemPairs();

        Assert.True(
            sample + differential >= RequiredTranslations,
            $"F10 asks for at least {RequiredTranslations} cross-ecosystem translations. The sample "
            + $"scenarios of {nameof(CrossFrameworkInputs)} carry {sample} of them whole and the "
            + $"differential matrix another {differential}. A framework brings three of each when it "
            + "enters, a differential query three, and a sample scenario three per source.");
    }

    /// <summary>
    /// And at least one end-to-end scenario for every pair of ecosystems - all four ordered
    /// pairs, because a matrix over two ecosystems has four cells and the two on the
    /// diagonal are translations as much as the others. The pairs are read from the
    /// differential matrix because that is where the fourth level of decision 016 is
    /// reached over a query; that every pair it states is really run by one suite or the
    /// other is <see cref="DifferentialMatrixTest"/>'s claim, and this one stands on it.
    /// </summary>
    [Fact]
    public void EveryPairOfEcosystemsHasAnEndToEndScenario()
    {
        var covered = DifferentialMatrix.Pairs()
            .Select(pair => (
                Source: FrameworkDescriptors.EcosystemOf(pair.Query.Source),
                Target: FrameworkDescriptors.EcosystemOf(pair.Target)))
            .Distinct()
            .ToList();

        foreach (var source in Enum.GetValues<Ecosystem>())
        {
            foreach (var target in Enum.GetValues<Ecosystem>())
            {
                Assert.True(
                    covered.Contains((source, target)),
                    $"No query of the differential matrix translates {source} into {target}, so that "
                    + "cell of F10's matrix has no scenario carried to a run.");
            }
        }
    }

    /// <summary>Every direction of the product that crosses the boundary, and only those.</summary>
    public static TheoryData<ORMEnum, ORMEnum> CrossDirections()
    {
        var data = new TheoryData<ORMEnum, ORMEnum>();
        foreach (var (source, target) in Directions())
        {
            data.Add(source, target);
        }

        return data;
    }

    private static IEnumerable<(ORMEnum Source, ORMEnum Target)> Directions()
        => from source in Enum.GetValues<ORMEnum>()
           from target in Enum.GetValues<ORMEnum>()
           where FrameworkDescriptors.CrossEcosystem(source, target)
           select (source, target);

    private static ConversionResult Translate(ORMEnum source, ORMEnum target)
        => ConversionHandler.Convert(source, target, CrossFrameworkInputs.Units(source));

    private static int WholeSampleTranslations()
        => Directions().Count(direction =>
        {
            var result = Translate(direction.Source, direction.Target);

            return result.Sources.Any(s => !s.ContentType.IsQuery())
                && result.Sources.Any(s => s.ContentType.IsQuery());
        });

    private static int DifferentialCrossEcosystemPairs()
        => DifferentialMatrix.Pairs()
            .Count(pair => FrameworkDescriptors.CrossEcosystem(pair.Query.Source, pair.Target));
}
