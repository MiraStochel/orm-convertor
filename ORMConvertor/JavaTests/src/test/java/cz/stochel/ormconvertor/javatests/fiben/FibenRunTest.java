package cz.stochel.ormconvertor.javatests.fiben;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.util.stream.IntStream;
import java.util.stream.Stream;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The on-demand run of the FIBEN catalog (decision 149) for the Java targets - the counterpart
 * of {@code FibenRunTest.cs}. A query translated as specified has to reach every target, its
 * artifact has to compile and to return the rows the catalog's own text returns; the text has
 * to return what the judge holds, where the database holds one; and the run is recorded. The
 * cases are stated without the instance, from the number of queries of the benchmark, so that
 * {@code SuiteSizeTest} counts them where nothing is configured. Without the Fiben database and
 * its instance every case skips with the reason, and fails where the environment promised them.
 */
@Tag("integration")
class FibenRunTest {

    static Stream<Arguments> cells() {
        return keys().flatMap(key -> Stream.of(Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS).map(framework -> Arguments.of(key, framework)));
    }

    static Stream<String> keys() {
        return IntStream.rangeClosed(1, FibenRun.QUERIES).mapToObj(number -> "q" + number);
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyQueryReachesEveryJavaTarget(String key, int framework) throws Exception {
        FibenRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        FibenCatalog.Query query = run.catalog().query(key);
        FibenRun.Cell cell = run.cell(key, framework);

        if (query.translation() == FibenCatalog.NOT_TRANSLATED) {
            assertNotNull(cell.refused, () -> key + " is marked as not translated, but " + Orm.nameOf(framework) + " translated it: " + cell.describe());
            return;
        }

        assertNull(cell.refused, cell::describe);
        assertNull(cell.unprepared, cell::describe);
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyTargetReturnsTheRowsOfTheSource(String key, int framework) throws Exception {
        FibenRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        FibenCatalog.Query query = run.catalog().query(key);
        if (query.translation() == FibenCatalog.NOT_TRANSLATED) {
            Assumptions.abort(key + " is not translated.");
        }

        FibenRun.Source source = run.source(key);
        FibenRun.Cell cell = run.cell(key, framework);

        assertNotNull(source.rows, () -> key + ": the text failed over JDBC: " + source.failed);
        assertNotNull(cell.rows, cell::describe);
        assertTrue(run.agreesWithSource(cell), () -> cell.describe() + " differs from the source: " + FibenRun.difference(source.rows, cell.rows));
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("keys")
    void theSourceReturnsTheRowsOfTheJudge(String key) throws Exception {
        FibenRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        if (!run.hasJudge()) {
            Assumptions.abort("The Fiben database holds no judge yet; the .NET suite's run makes one once it is documented (decision 149).");
        }

        FibenRun.Source source = run.source(key);
        assertNotNull(source.rows, () -> key + ": the text failed over JDBC: " + source.failed);
        assertEquals(Boolean.TRUE, run.agreesWithJudge(key), () -> key + ": the source differs from the judge: " + run.judgeDifference(key));
    }

    @Test
    void theRunIsRecorded() throws Exception {
        FibenRun run = FibenRun.current();
        run.skipIfUnavailable();

        assertEquals(run.catalog().dataSet(), run.dataSet(), "The catalog is written over another data set than the database holds.");
        for (String line : run.describe()) {
            System.out.println(line);
        }
    }

    @Test
    void aMissingDatabaseSkipsUnlessTheEnvironmentPromisedIt() {
        assertNull(FibenDatabase.failureOf("no Fiben", false));

        String failure = FibenDatabase.failureOf("no Fiben", true);
        assertNotNull(failure);
        assertTrue(failure.contains("ORMCONVERTOR_REQUIRE_FIBEN_DATABASE") && failure.contains("no Fiben"), failure);
    }

    @Test
    void theLimitIsAListOfKeys() {
        assertNull(FibenDatabase.resolveKeys(null));
        assertNull(FibenDatabase.resolveKeys(" "));
        assertEquals(java.util.Set.of("q1", "q2", "q237"), FibenDatabase.resolveKeys("q1, Q2 ,q237"));
    }

    @Test
    void theUrlHandedToACellCarriesTheTimeLimit() {
        String base = "jdbc:sqlserver://db:1433;databaseName=Fiben;user=sa;password=p;encrypt=true;trustServerCertificate=true";

        assertEquals(base + ";queryTimeout=" + FibenRun.QUERY_TIMEOUT_SECONDS, FibenDatabase.withQueryTimeout(base));
        assertEquals(base + ";queryTimeout=" + FibenRun.QUERY_TIMEOUT_SECONDS,
                FibenDatabase.withQueryTimeout("jdbc:sqlserver://db:1433;databaseName=Fiben;queryTimeout=30;user=sa;password=p;encrypt=true;trustServerCertificate=true"));
    }

    /** The digest of a result is the same in both suites; this value is held by the .NET suite over the same rows. */
    @Test
    void theDigestIsTheOneTheDotNetSuiteWrites() {
        assertEquals("rows=0 sha256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", FibenRun.digest(java.util.List.of()));
        assertEquals("rows=2 sha256=" + sha256("1\t\"a\"\n2\tNULL"), FibenRun.digest(java.util.List.of("1\t\"a\"", "2\tNULL")));
    }

    private static String sha256(String text) {
        try {
            byte[] hash = java.security.MessageDigest.getInstance("SHA-256").digest(text.getBytes(java.nio.charset.StandardCharsets.UTF_8));
            StringBuilder hex = new StringBuilder();
            for (byte b : hash) {
                hex.append(String.format(java.util.Locale.ROOT, "%02x", b));
            }
            return hex.toString();
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
