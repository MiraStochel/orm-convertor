package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The fourth verification level over a query (decisions 016 and 089): the generated query
 * is executed against the fixture and what came back is compared, field by field, with the
 * canonical result of that query.
 *
 * <p>Both halves of a pair really run - the source variant here or in the .NET suite, the
 * translated one in the suite that owns its framework - and they meet over the canonical
 * file rather than across a process boundary, because decision 089 refused to grow an
 * endpoint that runs foreign code for the sake of a test.
 *
 * <p>The canonical result belongs to the query and not to a direction, which is what keeps
 * it honest: a translation broken in one direction cannot be fixed by moving the target,
 * because moving it breaks every other direction of the same query at once.
 *
 * <p>Every case here is an integration test in the sense decision 087 gave the word - its
 * verdict depends on what happened in the database - so the count of F12 grows with the
 * matrix rather than being untouched by it.
 */
@Tag("integration")
class DifferentialVerificationTest {

    /**
     * Where a recording run writes the canonical results. It is a path and not a flag so
     * that recording cannot happen by accident, and only the source variant ever writes: a
     * result recorded from a translation would make the translation its own judge.
     */
    private static final String RECORD_VARIABLE = "ORMCONVERTOR_RECORD_DIFFERENTIAL";

    @BeforeAll
    static void prepare() throws Exception {
        ToolApi.awaitReady();
        TestSchema.create();
    }

    @AfterAll
    static void cleanUp() throws Exception {
        TestSchema.drop();
    }

    /** The queries whose source framework this suite can run. */
    static Stream<Arguments> sourceQueries() {
        return DifferentialMatrix.queries().stream()
                .filter(query -> JavaQueryRunner.owns(query.source()))
                .map(query -> Arguments.of(query.id()));
    }

    /**
     * The pairs of the matrix whose target this suite can run. The rest are the .NET
     * suite's, and each suite states that it ran every pair assigned to it
     * ({@link DifferentialMatrixTest}), so neither half can be met by skipping the other.
     */
    static Stream<Arguments> pairs() {
        return DifferentialMatrix.pairs().stream()
                .filter(pair -> JavaQueryRunner.owns(pair.target()))
                .map(pair -> Arguments.of(pair.queryId(), pair.target()));
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("sourceQueries")
    void theSourceVariantFixesTheCanonicalResult(String id) throws Exception {
        DifferentialQuery query = DifferentialMatrix.query(id);
        List<String> rows = JavaQueryRunner.run(query, query.source());

        String directory = recording();
        if (directory != null) {
            record(directory, query, rows);
            return;
        }

        assertEquals(query.canonicalResult(), rows);
    }

    @ParameterizedTest(name = "{0} -> {1}")
    @MethodSource("pairs")
    void theTranslatedVariantMatchesTheCanonicalResult(String id, int target) throws Exception {
        assumeTrue(recording() == null,
                "A recording run fixes the canonical results; comparing against them is the next run.");

        DifferentialQuery query = DifferentialMatrix.query(id);
        List<String> rows = JavaQueryRunner.run(query, target);

        assertEquals(query.canonicalResult(), rows,
                query.id() + ": " + Orm.nameOf(query.source()) + " -> " + Orm.nameOf(target));
    }

    private static String recording() {
        String directory = System.getenv(RECORD_VARIABLE);
        return directory == null || directory.isBlank() ? null : directory;
    }

    private static void record(String directory, DifferentialQuery query, List<String> rows) {
        try {
            Path results = Path.of(directory, "results");
            Files.createDirectories(results);

            // CRLF because the repository stores these as text files under its own
            // line-ending rule; the comparison is by line, so the choice affects the diff
            // and nothing else.
            List<String> lines = new ArrayList<>(rows);
            StringBuilder text = new StringBuilder();
            for (String line : lines) {
                text.append(line).append("\r\n");
            }

            Files.writeString(results.resolve(query.id() + ".txt"), text.toString(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException("The canonical result of " + query.id() + " could not be written.", e);
        }
    }
}
