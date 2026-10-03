package cz.stochel.ormconvertor.javatests.ldbc;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.differential.DifferentialMutation;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.util.Locale;
import java.util.stream.Stream;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The fourth verification level over the LDBC catalog for the Java targets, judged by LDBC's
 * own validation set for Interactive v1 (decision 117) - the counterpart of
 * {@code LdbcValidationTest.cs}, which runs the catalog's own text and the .NET targets. A
 * query translated as specified has to agree with the judge on every read in every target; a
 * query with a simplification is measured - the share of agreeing reads goes to the output -,
 * and its artifact still has to run.
 *
 * <p>The cases are stated without the instance, from the fixed list of the workload's queries,
 * so that {@code SuiteSizeTest} counts them where nothing is configured; whether a query is
 * judged or measured comes from the catalog the instance serves. Without LdbcSnb and its
 * instance every case skips with the reason, and fails where the environment promised them.
 */
@Tag("integration")
class LdbcValidationTest {

    static Stream<Arguments> cells() {
        return LdbcReplay.KEYS.stream()
                .flatMap(key -> Stream.of(Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS).map(framework -> Arguments.of(key, framework)));
    }

    static Stream<Arguments> mutations() {
        return DifferentialMutation.all().stream()
                .flatMap(mutation -> Stream.of(Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS).map(framework -> Arguments.of(mutation.key(), framework)));
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyReadAgreesWithTheJudgeOrIsMeasured(String key, int framework) throws Exception {
        LdbcReplay replay = LdbcReplay.current();
        replay.skipIfUnavailable();

        LdbcReplay.Tally tally = replay.tally(key, framework);
        String cell = key + " in " + Orm.nameOf(framework);

        assertNull(tally.unprepared, () -> tally.describe(cell));
        skipIfThePrefixHoldsNoRead(replay, tally, key);

        if (replay.catalog().query(key).translation() == LdbcCatalog.AS_SPECIFIED) {
            assertEquals(tally.reads, tally.matches, () -> tally.describe(cell));
            return;
        }

        assertEquals(0, tally.failures, () -> tally.describe(cell));
        System.out.println(String.format(Locale.ROOT, "%s: %d of %d reads agree with the judge (%.1f %%), set %s.",
                cell, tally.matches, tally.reads, 100.0 * tally.matches / tally.reads, replay.setName()));
        for (String disagreement : tally.disagreements) {
            System.out.println("  " + disagreement);
        }
    }

    /**
     * The negative half: a deliberately wrong artifact of one query (the mutations of decision
     * 089) disagrees with the judge on some read, or cannot even run.
     */
    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("mutations")
    void aMutatedArtifactDisagreesWithTheJudge(String mutation, int framework) throws Exception {
        LdbcReplay replay = LdbcReplay.current();
        replay.skipIfUnavailable();

        LdbcReplay.MutationTally tally = replay.mutation(mutation, framework);
        String cell = mutation + " of the " + Orm.nameOf(framework) + " artifact of " + LdbcReplay.MUTATED_QUERY;

        assertTrue(tally.changed, "The mutation " + cell + " changed nothing.");
        if (tally.detection == null && tally.reads == 0 && LdbcDatabase.rows() != null) {
            Assumptions.abort("The prefix of " + LdbcDatabase.rows() + " lines holds no read of " + LdbcReplay.MUTATED_QUERY + ".");
        }

        assertNotNull(tally.detection, "The mutation " + cell + " agreed with the judge on all " + tally.reads + " reads.");
        System.out.println(cell + ": " + tally.detection);
    }

    /** The compensation returns every LDBC table to the rows it was loaded with. */
    @Test
    void theCompensationReturnsTheDatabaseToItsLoadedState() throws Exception {
        LdbcReplay replay = LdbcReplay.current();
        replay.skipIfUnavailable();

        assertEquals(replay.loaded(), replay.restored());
        if (replay.inserts() > 0) {
            assertTrue(replay.replayed().entrySet().stream().anyMatch(table -> table.getValue() > replay.loaded().get(table.getKey())),
                    "The replay inserted " + replay.inserts() + " times and no table grew: " + replay.replayed());
        }

        System.out.println(String.format(Locale.ROOT, "%s: %d lines replayed (%d inserts) in %.1f min, %s.",
                replay.setName(), replay.lines(), replay.inserts(), replay.elapsed().toMillis() / 60000.0,
                LdbcDatabase.rows() != null ? "a prefix of " + LdbcDatabase.rows() : "the whole set"));
    }

    /** Without the judge's configuration a case skips with the reason; where it was promised, the same reason fails. */
    @Test
    void aMissingJudgeSkipsUnlessTheEnvironmentPromisedIt() {
        assertNull(LdbcDatabase.failureOf("no LdbcSnb", false));

        String failure = LdbcDatabase.failureOf("no LdbcSnb", true);
        assertNotNull(failure);
        assertTrue(failure.contains("ORMCONVERTOR_REQUIRE_LDBC_DATABASE") && failure.contains("no LdbcSnb"), failure);
    }

    /** A prefix is a positive number of lines; anything else stops the run rather than replaying something else. */
    @Test
    void thePrefixIsAPositiveNumberOfLines() {
        assertNull(LdbcDatabase.resolveRows(null));
        assertNull(LdbcDatabase.resolveRows(" "));
        assertEquals(2000, LdbcDatabase.resolveRows("2000"));

        for (String wrong : new String[] {"0", "-5", "many"}) {
            assertThrows(IllegalStateException.class, () -> LdbcDatabase.resolveRows(wrong), wrong);
        }
    }

    private static void skipIfThePrefixHoldsNoRead(LdbcReplay replay, LdbcReplay.Tally tally, String key) {
        if (tally.reads > 0) {
            return;
        }

        assertNotNull(LdbcDatabase.rows(), "The whole set " + replay.setName() + " was replayed and holds no read of " + key + ".");
        Assumptions.abort("The prefix of " + LdbcDatabase.rows() + " lines holds no read of " + key + ".");
    }
}
