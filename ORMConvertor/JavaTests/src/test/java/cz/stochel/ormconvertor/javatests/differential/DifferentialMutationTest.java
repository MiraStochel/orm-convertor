package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertNotEquals;

import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The negative half of F13: a deliberately wrong translation has to be detected
 * (decision 089). Every mutation the artifact can carry is applied to it, the mutated
 * artifact is run against the same fixture, and its result has to differ from the
 * canonical one.
 *
 * <p>Without this the whole matrix could be green for the wrong reason - a comparison that
 * always says yes says nothing - and two of the mutations test the comparison rather than
 * the translation: dropping an ordering is caught only by a comparison that takes order
 * seriously where the query gives one, and swapping two projected fields only by one that
 * pairs values with fields rather than counting them.
 */
@Tag("integration")
class DifferentialMutationTest {

    @BeforeAll
    static void prepare() throws Exception {
        ToolApi.awaitReady();
        TestSchema.create();
    }

    @AfterAll
    static void cleanUp() throws Exception {
        TestSchema.drop();
    }

    /**
     * Every (query, target, mutation) this suite runs. Which mutations a query carries is
     * the matrix's statement - a query with no ordering cannot lose one - and it is stated
     * rather than discovered, so nothing here can quietly stop running.
     */
    static Stream<Arguments> mutations() {
        return DifferentialMatrix.pairs().stream()
                .filter(pair -> JavaQueryRunner.owns(pair.target()))
                .flatMap(pair -> DifferentialMatrix.query(pair.queryId()).mutations().stream()
                        .map(key -> Arguments.of(pair.queryId(), pair.target(), key)));
    }

    @ParameterizedTest(name = "{0} -> {1}: {2}")
    @MethodSource("mutations")
    void aMutatedTranslationDoesNotMatchTheCanonicalResult(String id, int target, String mutationKey)
            throws Exception {

        DifferentialQuery query = DifferentialMatrix.query(id);
        ConversionResponse response = JavaQueryRunner.translate(query, target);

        String artifact = JavaQueryRunner.mutableArtifact(response, target, id);
        DifferentialMutation mutation = DifferentialMutation.of(mutationKey);
        String mutated = mutation.apply().apply(artifact);

        // Which mutations a query carries is stated in the matrix rather than discovered
        // here, so that one which stops reaching the artifact of some target is a failure
        // instead of a case that quietly stops running.
        assertNotEquals(artifact, mutated,
                id + " -> " + Orm.nameOf(target) + ": the matrix says this query carries \"" + mutationKey
                        + "\", and the rule changed nothing in the artifact:" + System.lineSeparator() + artifact);

        // A mutation is detected the moment the artifact stops answering with the canonical
        // result, and there are two ways for that to happen. It can run and return other
        // rows, which is the case the comparison is really about; or it can fail to compile,
        // to bind or to materialize, because a wrong query is often also an unusable one.
        // Both are the wrong translation being caught; only silence would not be.
        List<String> rows;
        try {
            rows = JavaQueryRunner.run(query, target, mutated);
        } catch (Exception detected) {
            return;
        }

        assertNotEquals(query.canonicalResult(), rows,
                id + " -> " + Orm.nameOf(target) + ": " + mutation.name() + ", and the result was the canonical one "
                        + "all the same. Either the fixture does not separate the two, or the comparison does not "
                        + "look at what the mutation changed.");
    }
}
