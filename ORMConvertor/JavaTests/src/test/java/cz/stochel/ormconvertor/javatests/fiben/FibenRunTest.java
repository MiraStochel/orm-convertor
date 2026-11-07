package cz.stochel.ormconvertor.javatests.fiben;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.catalog.CatalogQuery;
import cz.stochel.ormconvertor.javatests.catalog.CatalogRun;
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
 * The run is {@link CatalogRun}, the same mechanism the SEDE catalog runs by (decision 208);
 * what holds of it without a database is {@code catalog/CatalogRunTest}.
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
        CatalogRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        CatalogQuery query = run.query(key);
        CatalogRun.Cell cell = run.cell(key, framework);

        if (!query.translated()) {
            assertNotNull(cell.refused, () -> key + " is marked as not translated, but " + Orm.nameOf(framework) + " translated it: " + cell.describe());
            return;
        }

        assertNull(cell.refused, cell::describe);
        assertNull(cell.unprepared, cell::describe);
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyTargetReturnsTheRowsOfTheSource(String key, int framework) throws Exception {
        CatalogRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        if (!run.query(key).translated()) {
            Assumptions.abort(key + " is not translated.");
        }

        CatalogRun.Source source = run.source(key);
        CatalogRun.Cell cell = run.cell(key, framework);

        assertNotNull(source.rows, () -> key + ": the text failed over JDBC: " + source.failed);
        assertNotNull(cell.rows, cell::describe);
        assertTrue(run.agreesWithSource(cell), () -> cell.describe() + " differs from the source: " + CatalogRun.difference(source.rows, cell.rows));
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("keys")
    void theSourceReturnsTheRowsOfTheJudge(String key) throws Exception {
        CatalogRun run = FibenRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        if (!run.hasJudge()) {
            Assumptions.abort("The Fiben database holds no judge yet; the .NET suite's run makes one once it is documented (decision 149).");
        }

        if (!run.query(key).translated()) {
            Assumptions.abort(key + " is not translated, so the judge holds no entry for it.");
        }

        CatalogRun.Source source = run.source(key);
        assertNotNull(source.rows, () -> key + ": the text failed over JDBC: " + source.failed);
        assertEquals(Boolean.TRUE, run.agreesWithJudge(key), () -> key + ": the source differs from the judge: " + run.judgeDifference(key));
    }

    @Test
    void theRunIsRecorded() throws Exception {
        CatalogRun run = FibenRun.current();
        run.skipIfUnavailable();

        FibenCatalog catalog = run.catalog();
        assertEquals(catalog.dataSet(), run.dataSet(), "The catalog is written over another data set than the database holds.");
        for (String line : run.describe()) {
            System.out.println(line);
        }
    }
}
