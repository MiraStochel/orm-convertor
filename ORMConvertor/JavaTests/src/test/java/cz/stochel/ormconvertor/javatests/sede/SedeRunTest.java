package cz.stochel.ormconvertor.javatests.sede;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.catalog.CatalogQuery;
import cz.stochel.ormconvertor.javatests.catalog.CatalogRun;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.util.stream.Stream;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The on-demand run of the SEDE catalog (decision 208) for the Java targets - the counterpart
 * of {@code SedeRunTest.cs}. A query translated as its author wrote it has to reach every
 * target, its artifact has to compile and to return the rows the catalog's own text returns
 * with the catalog's values; the text has to return what the judge holds, where the database
 * holds one; and the run is recorded. The cases are stated without the instance, from the keys
 * of the catalog the repository carries, so that {@code SuiteSizeTest} counts them where
 * nothing is configured. Without the Sede database and its instance every case skips with the
 * reason, and fails where the environment promised them. The run is {@link CatalogRun}, the
 * same mechanism the FIBEN catalog runs by; what holds of it without a database is
 * {@code catalog/CatalogRunTest}.
 */
@Tag("integration")
class SedeRunTest {

    static Stream<Arguments> cells() {
        return keys().flatMap(key -> Stream.of(Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS).map(framework -> Arguments.of(key, framework)));
    }

    static Stream<String> keys() {
        return SedeCatalog.keysOfTheRepository().stream();
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyQueryReachesEveryJavaTarget(String key, int framework) throws Exception {
        CatalogRun run = SedeRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        CatalogQuery query = run.query(key);
        CatalogRun.Cell cell = run.cell(key, framework);

        if (!query.translated()) {
            Assumptions.abort(key + " is not translated: the catalog claims nothing of it, and the record says what " + Orm.nameOf(framework) + " did with it (" + cell.outcome() + ").");
        }

        assertNull(cell.refused, cell::describe);
        assertNull(cell.unprepared, cell::describe);
    }

    @ParameterizedTest(name = "{0} in {1}")
    @MethodSource("cells")
    void everyTargetReturnsTheRowsOfTheSource(String key, int framework) throws Exception {
        CatalogRun run = SedeRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        if (!run.query(key).translated()) {
            Assumptions.abort(key + " is not translated.");
        }

        CatalogRun.Source source = run.source(key);
        CatalogRun.Cell cell = run.cell(key, framework);

        assertTrue(source.ran(), () -> key + ": the text failed over JDBC: " + source.failed);
        assertTrue(cell.ran(), cell::describe);
        assertTrue(run.agreesWithSource(cell), () -> cell.describe() + " differs from the source: " + cell.sourceDifference);
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("keys")
    void theSourceReturnsTheRowsOfTheJudge(String key) throws Exception {
        CatalogRun run = SedeRun.current();
        run.skipIfUnavailable();
        run.skipIfNotCovered(key);

        if (!run.hasJudge()) {
            Assumptions.abort("The Sede database holds no judge yet; the .NET suite's run makes one once it is documented (decision 208).");
        }

        if (!run.query(key).translated()) {
            Assumptions.abort(key + " is not translated, so the judge holds no entry for it.");
        }

        CatalogRun.Source source = run.source(key);
        assertTrue(source.ran(), () -> key + ": the text failed over JDBC: " + source.failed);
        assertEquals(Boolean.TRUE, run.agreesWithJudge(key), () -> key + ": the source differs from the judge: " + run.judgeDifference(key));
    }

    @Test
    void theRunIsRecorded() throws Exception {
        CatalogRun run = SedeRun.current();
        run.skipIfUnavailable();

        SedeCatalog catalog = run.catalog();
        assertEquals(catalog.dataSet(), run.dataSet(), "The catalog is written over another data set than the database holds.");
        for (String line : run.describe()) {
            System.out.println(line);
        }
    }

    /** The keys the cases are stated from are the keys the repository carries, each once, keyed by the number in SEDE. */
    @Test
    void theKeysOfTheRepositoryAreTheKeysOfTheCatalog() {
        java.util.List<String> keys = SedeCatalog.keysOfTheRepository();

        assertTrue(!keys.isEmpty(), "The catalog the repository carries has no query.");
        assertEquals(keys.size(), keys.stream().distinct().count());
        assertTrue(keys.stream().allMatch(key -> key.matches("s[0-9]+")), "A key is not s<number>.");
    }
}
