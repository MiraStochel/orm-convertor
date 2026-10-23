package cz.stochel.ormconvertor.javatests.foreign;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import java.nio.file.Path;
import java.util.List;
import java.util.Map;
import java.util.TreeMap;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The run over foreign artifacts (decision 172) for the Java targets - the counterpart of
 * {@code ForeignArtifactRunTest.cs}: every artifact the environment names whose target this
 * suite owns goes through the levels and gets a row; the rows go to the directory the
 * environment names, rewritten after every level. A run on demand, never a test of a build:
 * without the variable the one case skips with the reason, and a red level is a row and not
 * a failure - only the path itself fails, by name. It needs no instance of the tool.
 */
@Tag("integration")
class ForeignArtifactRunTest {

    private static final String NONE = "";
    private static final String ROWS_FILE_NAME = "java-foreign-rows.tsv";
    private static final Map<String, ForeignVerdict> VERDICTS = new TreeMap<>();

    static Stream<String> artifacts() {
        String root = configured();
        if (root == null) {
            return Stream.of(NONE);
        }

        List<String> owned = ForeignArtifact.discover(Path.of(root), TestDatabase.schemaName()).stream()
                .filter(artifact -> ForeignArtifactRun.owns(artifact.target()))
                .map(artifact -> artifact.directory().toString())
                .toList();

        return owned.isEmpty() ? Stream.of(NONE) : owned.stream();
    }

    @BeforeAll
    static void prepare() throws Exception {
        if (configured() != null) {
            TestSchema.create();
        }
    }

    @AfterAll
    static void cleanUp() throws Exception {
        if (configured() != null) {
            TestSchema.drop();
        }
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("artifacts")
    void theArtifactIsJudgedAndRecorded(String directory) throws Exception {
        if (directory.isEmpty()) {
            Assumptions.abort("No foreign artifact of a Java target configured: set " + ForeignArtifact.ARTIFACTS_VARIABLE
                    + " to an artifact or a directory of artifacts (decision 172).");
        }

        ForeignArtifact artifact = ForeignArtifact.read(Path.of(directory), TestDatabase.schemaName());
        ForeignVerdict verdict = ForeignArtifactRun.run(artifact, ForeignArtifactRunTest::record);
        System.out.println(verdict.describe());
    }

    private static String configured() {
        String root = System.getenv(ForeignArtifact.ARTIFACTS_VARIABLE);
        return root == null || root.isBlank() ? null : root.strip();
    }

    private static synchronized void record(ForeignVerdict verdict) {
        VERDICTS.put(verdict.artifact(), verdict);

        String directory = System.getenv(ForeignArtifact.RESULTS_VARIABLE);
        if (directory != null && !directory.isBlank()) {
            ForeignVerdict.write(Path.of(directory.strip()), ROWS_FILE_NAME, VERDICTS.values());
        }
    }
}
