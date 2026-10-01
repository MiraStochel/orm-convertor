package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assumptions.assumeTrue;

import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.QueryFeature;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;
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
 * because moving it breaks every other direction of the same query at once. A category of
 * T2 has several sources, and every one of their runs meets the same file too.
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
        JavaQueryRunner.closeAll();
        TestSchema.drop();
    }

    /** The source variants this suite can run: every (query, source) whose source it owns. */
    static Stream<Arguments> sourceVariants() {
        return DifferentialMatrix.queries().stream()
                .flatMap(query -> query.sources().stream()
                        .filter(JavaQueryRunner::owns)
                        .map(source -> Arguments.of(query.id(), source)));
    }

    /**
     * The pairs of the matrix whose target this suite can run. The rest are the .NET
     * suite's, and each suite states that it ran every pair assigned to it
     * ({@link DifferentialMatrixTest}), so neither half can be met by skipping the other.
     */
    static Stream<Arguments> pairs() {
        return DifferentialMatrix.pairs().stream()
                .filter(pair -> JavaQueryRunner.owns(pair.target()))
                .map(pair -> Arguments.of(pair.queryId(), pair.source(), pair.target()));
    }

    @ParameterizedTest(name = "{0} from {1}")
    @MethodSource("sourceVariants")
    void theSourceVariantFixesTheCanonicalResult(String id, int source) throws Exception {
        DifferentialQuery query = DifferentialMatrix.query(id);
        List<String> rows = JavaQueryRunner.run(query, source, source);

        String directory = recording();
        if (directory != null) {
            record(directory, query, source, rows);
            return;
        }

        assertEquals(query.canonicalResult(), rows, query.id() + " from " + Orm.nameOf(source));
    }

    @ParameterizedTest(name = "{0}: {1} -> {2}")
    @MethodSource("pairs")
    void theTranslatedVariantMatchesTheCanonicalResult(String id, int source, int target) throws Exception {
        assumeTrue(recording() == null,
                "A recording run fixes the canonical results; comparing against them is the next run.");

        DifferentialQuery query = DifferentialMatrix.query(id);
        List<String> rows = JavaQueryRunner.run(query, source, target);

        assertEquals(query.canonicalResult(), rows,
                query.id() + ": " + Orm.nameOf(source) + " -> " + Orm.nameOf(target));
    }

    /**
     * A pair whose target falls back writes the query in native SQL, as the matrix states
     * (decision 113): a record of kind Fallback names the feature, and the bare SQL stands
     * beside the method. Its rows are judged by the pairs above with every other pair - the
     * fourth level measures the escape path as it measures a translation -, and this keeps
     * the two values of a cell from passing as each other.
     */
    @Test
    void aFallbackDirectionFallsBackAsStated() {
        for (DifferentialMatrix.FallbackDirection fallback : DifferentialMatrix.fallbackDirections()) {
            if (!JavaQueryRunner.owns(fallback.target())) {
                continue;
            }

            DifferentialQuery query = DifferentialMatrix.query(fallback.queryId());
            ConversionResponse response = JavaQueryRunner.translate(query, fallback.source(), fallback.target());
            String direction = fallback.queryId() + ": " + Orm.nameOf(fallback.source()) + " -> " + Orm.nameOf(fallback.target());

            assertTrue(response.records().stream().anyMatch(record ->
                            record.kind() == RecordKind.FALLBACK && record.feature() != null && record.feature() == fallback.feature()),
                    direction + " is stated as falling back and no Fallback record names " + QueryFeature.nameOf(fallback.feature())
                            + ":" + System.lineSeparator() + response.describeRecords());
            assertFalse(response.artifactsOf(ContentType.SQL_QUERY).isEmpty(),
                    direction + " is stated as falling back and no native SQL came out beside the method.");
        }
    }

    /**
     * A direction the matrix states as refused is refused: no query artifact comes out and a
     * record names the feature (decision 053). No category is refused since decision 113
     * turned the three refusals into fallbacks, so this loop is empty until one is, and then
     * it is not.
     */
    @Test
    void aRefusedDirectionIsRefusedAsStated() {
        for (DifferentialMatrix.RefusedDirection refused : DifferentialMatrix.refusedDirections()) {
            if (!JavaQueryRunner.owns(refused.target())) {
                continue;
            }

            DifferentialQuery query = DifferentialMatrix.query(refused.queryId());
            ToolResponse answer = ToolApi.convert(refused.source(), refused.target(), query.units(refused.source()));
            assertEquals(200, answer.statusCode(), answer.body());
            ConversionResponse response = answer.required();

            assertTrue(response.artifactsOf(ContentType.JAVA_QUERY).isEmpty()
                            && response.artifactsOf(ContentType.JPQL_QUERY).isEmpty(),
                    refused.queryId() + ": " + Orm.nameOf(refused.source()) + " -> " + Orm.nameOf(refused.target())
                            + " is stated as refused and a query artifact came out.");
            assertTrue(response.records().stream().anyMatch(record ->
                            record.feature() != null && record.feature() == refused.feature()),
                    refused.queryId() + ": no record names " + QueryFeature.nameOf(refused.feature()) + ":"
                            + System.lineSeparator() + response.describeRecords());
        }
    }

    private static String recording() {
        String directory = System.getenv(RECORD_VARIABLE);
        return directory == null || directory.isBlank() ? null : directory;
    }

    /**
     * Writes the canonical result of the query. A category is stated by several sources and
     * every one of them records into the same file, so a second recording of a file has to
     * agree with the first: two sources that disagree about the rows are two queries, and a
     * file written by whichever ran last would hide that.
     */
    private static void record(String directory, DifferentialQuery query, int source, List<String> rows) {
        try {
            Path results = Path.of(directory, "results");
            Files.createDirectories(results);

            // CRLF because the repository stores these as text files under its own
            // line-ending rule; the comparison is by line, so the choice affects the diff
            // and nothing else.
            StringBuilder text = new StringBuilder();
            for (String line : rows) {
                text.append(line).append("\r\n");
            }

            Path file = results.resolve(query.id() + ".txt");
            if (Files.exists(file)) {
                assertEquals(Files.readString(file, StandardCharsets.UTF_8), text.toString(),
                        query.id() + ": the source variant of " + Orm.nameOf(source) + " returned other rows than "
                                + "the source variant recorded before it into " + file + ", so the sources do not state one query.");
                return;
            }

            Files.writeString(file, text.toString(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException("The canonical result of " + query.id() + " could not be written.", e);
        }
    }
}
