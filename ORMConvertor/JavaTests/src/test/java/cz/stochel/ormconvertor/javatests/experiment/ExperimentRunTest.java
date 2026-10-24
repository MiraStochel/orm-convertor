package cz.stochel.ormconvertor.javatests.experiment;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.fiben.FibenDatabase;
import cz.stochel.ormconvertor.javatests.foreign.ForeignArtifact;
import cz.stochel.ormconvertor.javatests.foreign.ForeignArtifactRun;
import cz.stochel.ormconvertor.javatests.foreign.ForeignVerdict;
import cz.stochel.ormconvertor.javatests.ldbc.LdbcDatabase;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.ConversionRecord;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.ConversionUnit;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Collectors;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/**
 * The experiment run of the Java suite (decision 173) - the counterpart of
 * {@code ExperimentRunTest.cs}: every input the .NET suite exported into {@code inputs/} under
 * {@code ORMCONVERTOR_EXPERIMENT_RESULTS} is translated into every Java target by the instance
 * whose catalog the key needs (decision 078) - the suite's own for the matrix, the scenarios and
 * the examples, the LDBC instance for an LDBC key, the FIBEN instance for a FIBEN key - and the
 * artifact goes through the levels of the path of decision 172 as a foreign artifact would, with
 * the same row, producer {@code tool}, into {@code java-tool-rows.tsv}. A key whose instance is
 * not configured is left out; a translation with a Failure record is the row {@code refused} at
 * level 0. A run on demand, never a test of a build: without the variable the one case skips.
 */
@Tag("integration")
class ExperimentRunTest {

    private static final String NONE = "";
    private static final String ROWS_FILE_NAME = "java-tool-rows.tsv";
    private static final String PRODUCER = "tool";
    private static final Map<String, ForeignVerdict> VERDICTS = new TreeMap<>();

    static Stream<Arguments> translations() {
        String results = results();
        if (results == null) {
            return Stream.of(Arguments.of(NONE, Orm.HIBERNATE));
        }

        Path inputs = inputsDirectory(results);
        Set<String> kinds = kinds();

        List<Arguments> arguments = new ArrayList<>();
        for (ExperimentInput input : ExperimentInput.discover(inputs, TestDatabase.schemaName())) {
            if (!kinds.contains(input.key().kind())) {
                continue;
            }
            for (int target : new int[] {Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS}) {
                arguments.add(Arguments.of(input.directory().toString(), target));
            }
        }

        return arguments.isEmpty() ? Stream.of(Arguments.of(NONE, Orm.HIBERNATE)) : arguments.stream();
    }

    @BeforeAll
    static void prepare() throws Exception {
        if (results() != null) {
            ToolApi.awaitReady();
            TestSchema.create();
        }
    }

    @AfterAll
    static void cleanUp() throws Exception {
        if (results() != null) {
            JavaQueryRunner.closeAll();
            TestSchema.drop();
        }
    }

    @ParameterizedTest(name = "{0} -> {1}")
    @MethodSource("translations")
    void theToolsArtifactIsJudgedAndRecorded(String directory, int target) throws Exception {
        if (directory.isEmpty()) {
            Assumptions.abort("No experiment run configured: set " + ExperimentRunTest.RESULTS_VARIABLE + " to the directory of the run (decision 173).");
        }

        ExperimentInput input = ExperimentInput.read(Path.of(directory), TestDatabase.schemaName());
        ForeignVerdict verdict = judge(input, target);
        System.out.println(verdict.describe());
    }

    /** The directory of a run; the inputs stand under it unless {@code ORMCONVERTOR_EXPERIMENT_INPUTS} names them elsewhere. */
    static final String RESULTS_VARIABLE = "ORMCONVERTOR_EXPERIMENT_RESULTS";
    static final String INPUTS_VARIABLE = "ORMCONVERTOR_EXPERIMENT_INPUTS";
    static final String KINDS_VARIABLE = "ORMCONVERTOR_EXPERIMENT_KINDS";

    private static String results() {
        String results = System.getenv(RESULTS_VARIABLE);
        return results == null || results.isBlank() ? null : results.strip();
    }

    private static Path inputsDirectory(String results) {
        String inputs = System.getenv(INPUTS_VARIABLE);
        return inputs == null || inputs.isBlank() ? Path.of(results, "inputs") : Path.of(inputs.strip());
    }

    /** The kinds of keys this environment can translate: an LDBC key needs the LDBC instance, a FIBEN key the FIBEN instance and database. */
    private static Set<String> kinds() {
        String asked = System.getenv(KINDS_VARIABLE);
        Set<String> kinds = asked == null || asked.isBlank()
                ? new HashSet<>(List.of(ForeignArtifact.Key.MATRIX, ForeignArtifact.Key.CROSS, ForeignArtifact.Key.EXAMPLE, ForeignArtifact.Key.LDBC, ForeignArtifact.Key.FIBEN))
                : Stream.of(asked.split(",")).map(kind -> kind.strip().toLowerCase(Locale.ROOT)).filter(kind -> !kind.isEmpty()).collect(Collectors.toCollection(HashSet::new));

        if (LdbcDatabase.apiUrl() == null) {
            kinds.remove(ForeignArtifact.Key.LDBC);
        }
        if (FibenDatabase.apiUrl() == null || FibenDatabase.jdbcUrl() == null) {
            kinds.remove(ForeignArtifact.Key.FIBEN);
        }

        return kinds;
    }

    /** The instance a key is translated by (decision 078): the one whose catalog holds the tables the key reads. */
    private static String instanceOf(ForeignArtifact.Key key) {
        return switch (key.kind()) {
            case ForeignArtifact.Key.LDBC -> LdbcDatabase.apiUrl();
            case ForeignArtifact.Key.FIBEN -> FibenDatabase.apiUrl();
            default -> ToolApi.baseUrl();
        };
    }

    /**
     * Translates the input into the target by the instance and judges the artifact. A translation
     * with a Failure record is the row {@code refused} at level 0, whatever the tool handed over
     * beside the refusal (as the FIBEN run reads a cell); otherwise the artifact is judged as a
     * foreign artifact of producer {@code tool} is.
     */
    static ForeignVerdict judge(ExperimentInput input, int target) throws Exception {
        String rowId = input.rowId(target);
        String answers = input.key().toString();
        String source = Orm.nameOf(input.source());
        String targetName = Orm.nameOf(target);

        ToolResponse answer = ToolApi.convert(instanceOf(input.key()), input.source(), target, input.units());
        if (answer.statusCode() != 200) {
            ForeignVerdict refused = ForeignVerdict.refused(rowId, answers, source, targetName, PRODUCER,
                    "the instance answered " + answer.statusCode() + ": " + answer.body(), "{}");
            record(refused);
            return refused;
        }

        ConversionResponse conversion = answer.required();
        String metadata = metadata(conversion);

        List<ConversionRecord> failures = conversion.recordsOf(RecordKind.FAILURE);
        if (!failures.isEmpty()) {
            String reasons = failures.stream().map(ConversionRecord::reason).distinct().collect(Collectors.joining("; "));
            ForeignVerdict refused = ForeignVerdict.refused(rowId, answers, source, targetName, PRODUCER, reasons, metadata);
            record(refused);
            return refused;
        }

        Set<String> taken = new HashSet<>();
        List<ForeignArtifact.Unit> units = new ArrayList<>();
        int index = 0;
        for (ConversionUnit unit : conversion.sources()) {
            index++;
            String name = unit.name() == null || unit.name().isBlank()
                    ? ContentType.nameOf(unit.contentType()) + "-" + index + extensionOf(unit.contentType())
                    : unit.name();
            units.add(new ForeignArtifact.Unit(unique(name, taken), unit.contentType(), unit.content()));
        }

        ForeignArtifact artifact = new ForeignArtifact(rowId, input.directory(), input.key(), input.source(), target, PRODUCER, units, metadata);
        return ForeignArtifactRun.run(artifact, ExperimentRunTest::record);
    }

    /** What the row of a tool artifact carries as metadata: the versions the run record of S6 names, and whether the query went out in native SQL. */
    private static String metadata(ConversionResponse conversion) {
        Map<String, String> metadata = new LinkedHashMap<>();
        metadata.put("toolVersion", conversion.toolVersion());
        metadata.put("sourceFrameworkVersion", conversion.sourceFrameworkVersion());
        metadata.put("targetFrameworkVersion", conversion.targetFrameworkVersion());
        metadata.put("catalogState", String.valueOf(conversion.catalogState()));

        List<ConversionRecord> fallbacks = conversion.recordsOf(RecordKind.FALLBACK);
        if (!fallbacks.isEmpty()) {
            metadata.put("fallback", fallbacks.stream().map(record -> record.feature() == null ? "-" : String.valueOf(record.feature())).distinct().collect(Collectors.joining(", ")));
        }

        StringBuilder json = new StringBuilder("{");
        boolean first = true;
        for (Map.Entry<String, String> entry : metadata.entrySet()) {
            if (!first) {
                json.append(',');
            }
            first = false;
            json.append('"').append(entry.getKey()).append("\":\"").append(entry.getValue() == null ? "" : entry.getValue().replace("\\", "\\\\").replace("\"", "\\\"")).append('"');
        }
        return json.append('}').toString();
    }

    private static String extensionOf(int contentType) {
        return switch (contentType) {
            case ContentType.CSHARP, ContentType.CSHARP_ENTITY, ContentType.CSHARP_QUERY, ContentType.CSHARP_LINQ_QUERY -> ".cs";
            case ContentType.JAVA, ContentType.JAVA_ENTITY, ContentType.JAVA_QUERY -> ".java";
            case ContentType.XML -> ".xml";
            case ContentType.SQL_QUERY -> ".sql";
            case ContentType.HQL_QUERY -> ".hql";
            case ContentType.JPQL_QUERY -> ".jpql";
            default -> ".txt";
        };
    }

    private static String unique(String name, Set<String> taken) {
        if (taken.add(name.toLowerCase(Locale.ROOT))) {
            return name;
        }

        int dot = name.lastIndexOf('.');
        String stem = dot < 0 ? name : name.substring(0, dot);
        String extension = dot < 0 ? "" : name.substring(dot);
        for (int number = 2; ; number++) {
            String candidate = stem + "-" + number + extension;
            if (taken.add(candidate.toLowerCase(Locale.ROOT))) {
                return candidate;
            }
        }
    }

    private static synchronized void record(ForeignVerdict verdict) {
        VERDICTS.put(verdict.artifact(), verdict);

        String results = results();
        if (results != null) {
            ForeignVerdict.write(Path.of(results), ROWS_FILE_NAME, VERDICTS.values());
        }
    }
}
