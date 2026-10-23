package cz.stochel.ormconvertor.javatests.foreign;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.differential.DifferentialMatrix;
import cz.stochel.ormconvertor.javatests.differential.DifferentialQuery;
import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.JavaSources;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import java.io.IOException;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The path of a foreign artifact itself (decision 172), held by the artifacts the repository
 * carries under {@code Tests/Database/Foreign} - the counterpart of
 * {@code ForeignArtifactPathTest.cs}, over the artifacts whose target this suite owns: every
 * one of them has to end as the row {@code expected.txt} states, never as a failure. The
 * tool's own artifact goes through the path too, to show that the path measures what the
 * client measures; and what is not an artifact but a misconfiguration fails by name.
 */
@Tag("integration")
class ForeignArtifactPathTest {

    /** What an own artifact is written to reach. */
    record Expected(String id, int reached, String outcome) {
    }

    static Path ownArtifacts() throws Exception {
        URL expected = ForeignArtifactPathTest.class.getResource("/Foreign/expected.txt");
        if (expected == null) {
            throw new IllegalStateException("Test resource /Foreign/expected.txt is missing: the pom reads it from ../Tests/Database.");
        }
        return Path.of(expected.toURI()).getParent();
    }

    static List<Expected> expected() throws Exception {
        List<Expected> result = new ArrayList<>();
        String id = null;
        Map<String, String> values = new LinkedHashMap<>();

        for (String raw : Files.readAllLines(ownArtifacts().resolve("expected.txt"), StandardCharsets.UTF_8)) {
            String line = raw.strip();
            if (line.isEmpty() || line.startsWith("#")) {
                continue;
            }
            if (line.startsWith("[") && line.endsWith("]")) {
                if (id != null) {
                    result.add(new Expected(id, Integer.parseInt(values.get("reached")), values.get("outcome")));
                }
                id = line.substring(1, line.length() - 1).strip();
                values = new LinkedHashMap<>();
                continue;
            }
            int separator = line.indexOf('=');
            values.put(line.substring(0, separator).strip(), line.substring(separator + 1).strip());
        }
        if (id != null) {
            result.add(new Expected(id, Integer.parseInt(values.get("reached")), values.get("outcome")));
        }

        return result;
    }

    static Stream<String> own() throws Exception {
        Path root = ownArtifacts();
        return expected().stream()
                .filter(expected -> ForeignArtifactRun.owns(ForeignArtifact.read(root.resolve(expected.id()), "schema").target()))
                .map(Expected::id);
    }

    static Stream<Arguments> toolPairs() {
        return Stream.of(
                Arguments.of("q1-entity-filter-ordered", Orm.DAPPER, Orm.HIBERNATE),
                Arguments.of("q1-entity-filter-ordered", Orm.DAPPER, Orm.ECLIPSELINK),
                Arguments.of("q1-entity-filter-ordered", Orm.DAPPER, Orm.MYBATIS));
    }

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

    @ParameterizedTest(name = "{0}")
    @MethodSource("own")
    void theOwnArtifactEndsAsTheRowItWasWrittenToEnd(String id) throws Exception {
        Expected expected = expected().stream().filter(candidate -> candidate.id().equals(id)).findFirst().orElseThrow();
        ForeignArtifact artifact = ForeignArtifact.read(ownArtifacts().resolve(id), TestDatabase.schemaName());

        ForeignVerdict verdict = ForeignArtifactRun.run(artifact, null);
        System.out.println(verdict.describe());

        assertEquals(expected.reached() + " " + expected.outcome(), verdict.reached() + " " + verdict.outcome(), verdict.describe());
    }

    /**
     * The tool's own translation, put through the path as an artifact of producer {@code tool},
     * reaches the fourth level and agrees - the same verdict the client gives the same
     * translation. The one time an artifact of the tool goes this way (decision 172).
     */
    @ParameterizedTest(name = "{0} from {1} into {2}")
    @MethodSource("toolPairs")
    void theToolsOwnArtifactPassesThePathAsItPassesTheClient(String id, int source, int target) throws Exception {
        DifferentialQuery query = DifferentialMatrix.query(id);
        ConversionResponse response = JavaQueryRunner.translate(query, source, target);

        Path temp = Files.createTempDirectory("ormconvertor-foreign-");
        try {
            List<ForeignArtifact.Unit> units = new ArrayList<>();
            int index = 0;
            for (ConversionUnit unit : response.sources()) {
                index++;
                String name = switch (unit.contentType()) {
                    case ContentType.JAVA_ENTITY -> JavaSources.publicClassOf(unit.content()) + ".java";
                    case ContentType.JAVA_QUERY -> "Query-" + index + ".java";
                    case ContentType.XML -> "Mapper-" + index + ".xml";
                    case ContentType.JPQL_QUERY -> "Query-" + index + ".jpql";
                    case ContentType.SQL_QUERY -> "Query-" + index + ".sql";
                    default -> "Unit-" + index + ".txt";
                };
                units.add(new ForeignArtifact.Unit(name, unit.contentType(), unit.content()));
            }

            Path directory = temp.resolve(id + "-" + Orm.nameOf(target).toLowerCase() + "-tool");
            ForeignArtifact.write(directory, new ForeignArtifact.Key(ForeignArtifact.Key.MATRIX, id), source, target, "tool", units,
                    "{\"toolVersion\": \"" + response.toolVersion() + "\"}");

            ForeignVerdict verdict = ForeignArtifactRun.run(ForeignArtifact.read(directory, TestDatabase.schemaName()), null);
            System.out.println(verdict.describe());

            assertEquals(ForeignVerdict.LEVEL_RAN, verdict.reached(), verdict.describe());
            assertEquals(ForeignVerdict.AGREES, verdict.outcome(), verdict.describe());

            // The client's verdict over the same translation: the same rows against the same measure.
            assertEquals(query.canonicalResult(), JavaQueryRunner.run(query, source, target));
        } finally {
            delete(temp);
        }
    }

    /**
     * A scenario key (decision 172): the entity of the tool's own translation alone, under
     * {@code cross:<source>}, goes through the path up to the third level and the row says the
     * scenario has no fourth - never a failure.
     */
    @Test
    void aScenarioArtifactEndsAtTheThirdLevel() throws Exception {
        DifferentialQuery query = DifferentialMatrix.query("q1-entity-filter-ordered");
        ConversionResponse response = JavaQueryRunner.translate(query, Orm.DAPPER, Orm.HIBERNATE);

        Path temp = Files.createTempDirectory("ormconvertor-foreign-");
        try {
            List<ForeignArtifact.Unit> units = new ArrayList<>();
            for (ConversionUnit unit : response.artifactsOf(ContentType.JAVA_ENTITY)) {
                units.add(new ForeignArtifact.Unit(JavaSources.publicClassOf(unit.content()) + ".java", unit.contentType(), unit.content()));
            }

            Path directory = temp.resolve("cross-dapper-hibernate-tool");
            ForeignArtifact.write(directory, new ForeignArtifact.Key(ForeignArtifact.Key.CROSS, "Dapper"), Orm.DAPPER, Orm.HIBERNATE, "tool", units, null);

            ForeignVerdict verdict = ForeignArtifactRun.run(ForeignArtifact.read(directory, TestDatabase.schemaName()), null);
            System.out.println(verdict.describe());

            assertEquals(ForeignVerdict.LEVEL_ACCEPTED, verdict.reached(), verdict.describe());
            assertEquals(ForeignVerdict.ACCEPTED, verdict.outcome(), verdict.describe());
        } finally {
            delete(temp);
        }
    }

    @Test
    void aManifestWithoutAKeyFailsByName() throws Exception {
        IllegalStateException error = assertThrows(IllegalStateException.class,
                () -> readManifest("{ \"source\": \"Dapper\", \"target\": \"Hibernate\", \"producer\": \"human\", \"units\": [] }"));
        assertTrue(error.getMessage().contains("answers"), error.getMessage());
    }

    @Test
    void aKindNobodyMeasuresFailsByName() throws Exception {
        IllegalStateException error = assertThrows(IllegalStateException.class,
                () -> readManifest("{ \"answers\": \"scenario:x\", \"source\": \"Dapper\", \"target\": \"Hibernate\", \"producer\": \"human\", \"units\": [{ \"path\": \"a.sql\" }] }"));
        assertTrue(error.getMessage().contains("scenario") && error.getMessage().contains("matrix"), error.getMessage());
    }

    @Test
    void anArtifactOfTheOtherSuiteIsNotOwned() throws Exception {
        ForeignArtifact artifact = readManifest(
                "{ \"answers\": \"matrix:q1-entity-filter-ordered\", \"source\": \"Dapper\", \"target\": \"EFCore\", \"producer\": \"human\", \"units\": [{ \"path\": \"a.cs\", \"contentType\": \"CSharpEntity\" }] }",
                "a.cs", "class A {}");

        assertFalse(ForeignArtifactRun.owns(artifact.target()));
        assertThrows(IllegalStateException.class, () -> ForeignArtifactRun.run(artifact, null));
    }

    private static ForeignArtifact readManifest(String manifest, String... files) throws IOException {
        Path temp = Files.createTempDirectory("ormconvertor-foreign-");
        try {
            Files.writeString(temp.resolve(ForeignArtifact.MANIFEST_FILE_NAME), manifest, StandardCharsets.UTF_8);
            for (int index = 0; index + 1 < files.length; index += 2) {
                Files.writeString(temp.resolve(files[index]), files[index + 1], StandardCharsets.UTF_8);
            }
            return ForeignArtifact.read(temp, TestDatabase.schemaName());
        } finally {
            delete(temp);
        }
    }

    private static void delete(Path root) throws IOException {
        try (Stream<Path> tree = Files.walk(root)) {
            tree.sorted(Comparator.reverseOrder()).forEach(path -> {
                try {
                    Files.deleteIfExists(path);
                } catch (IOException e) {
                    // A leftover in the temporary directory is not worth a failure.
                }
            });
        }
    }
}
