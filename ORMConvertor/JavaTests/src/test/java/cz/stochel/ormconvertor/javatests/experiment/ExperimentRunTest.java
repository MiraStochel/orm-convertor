package cz.stochel.ormconvertor.javatests.experiment;

import cz.stochel.ormconvertor.javatests.CaseStudyDatabase;
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
import java.io.IOException;
import java.nio.file.Files;
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

    /** Rows of the file this run does not write and keeps: the series points of the walk with the other catalog (decision 182), which shares the directory of the run. */
    private static final Map<String, String> KEPT = new TreeMap<>();

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
            // A case study needs the instance over the case-study database (decision 176); without
            // it the input is left out, as an LDBC key is without the LDBC instance.
            if (isCaseStudy(input.key()) && !CaseStudyDatabase.isConfigured()) {
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
            if (kinds().contains(ForeignArtifact.Key.SERIES) && CATALOG_NONE.equals(seriesCatalog())) {
                ToolApi.awaitReady(dryInstance());
            }
            KEPT.putAll(rowsOfTheOtherCatalog(Path.of(results(), ROWS_FILE_NAME)));
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

    /** The catalog of the series walk (decision 182), as the .NET half takes it: {@code fixture} (the default) or {@code none}, dry. */
    static final String SERIES_CATALOG_VARIABLE = "ORMCONVERTOR_SERIES_CATALOG";

    /** The instance with no catalog configured, which translates the series of a dry walk. */
    static final String DRY_API_URL_VARIABLE = "ORMCONVERTOR_DRY_API_URL";

    private static final String CATALOG_FIXTURE = "fixture";
    private static final String CATALOG_NONE = "none";

    /** {@code CatalogConnectionState.NotConfigured}: the instance has no catalog and translated on conventions. */
    private static final int CATALOG_NOT_CONFIGURED = 0;

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

    /** The catalog of the series walk, read as the .NET half reads it. */
    private static String seriesCatalog() {
        String catalog = System.getenv(SERIES_CATALOG_VARIABLE);
        return catalog != null && catalog.strip().equalsIgnoreCase(CATALOG_NONE) ? CATALOG_NONE : CATALOG_FIXTURE;
    }

    /** The instance of a dry walk; a dry walk without one fails by name rather than translating with the fixture as the catalog. */
    private static String dryInstance() {
        String url = System.getenv(DRY_API_URL_VARIABLE);
        if (url == null || url.isBlank()) {
            throw new IllegalStateException(SERIES_CATALOG_VARIABLE + "=" + CATALOG_NONE + " translates the series by an instance with no catalog, named by "
                    + DRY_API_URL_VARIABLE + ", and it is not set (decision 182).");
        }
        return url.strip();
    }

    /**
     * The rows of the file that belong to the series walk with the other catalog: the two walks
     * of one run share its directory, and the rows of the one walked first stay beside those of
     * the second. Every other row of the file is this run's to write again.
     */
    private static Map<String, String> rowsOfTheOtherCatalog(Path file) throws IOException {
        Map<String, String> kept = new TreeMap<>();
        if (!Files.exists(file)) {
            return kept;
        }

        String other = "-" + (CATALOG_NONE.equals(seriesCatalog()) ? CATALOG_FIXTURE : CATALOG_NONE);
        List<String> lines = Files.readAllLines(file);
        for (String line : lines.subList(Math.min(1, lines.size()), lines.size())) {
            String artifact = line.split("\t", 2)[0];
            if (artifact.startsWith(ForeignArtifact.Key.SERIES + "-") && artifact.endsWith(other)) {
                kept.put(artifact, line);
            }
        }
        return kept;
    }

    /** The instance a key is translated by (decision 078): the one whose catalog holds the tables the key reads - for a case study the case-study database (decision 176), for a series point of a dry walk the instance with none. */
    private static String instanceOf(ForeignArtifact.Key key) {
        return switch (key.kind()) {
            case ForeignArtifact.Key.LDBC -> LdbcDatabase.apiUrl();
            case ForeignArtifact.Key.FIBEN -> FibenDatabase.apiUrl();
            case ForeignArtifact.Key.EXAMPLE -> isCaseStudy(key) ? CaseStudyDatabase.apiUrl() : ToolApi.baseUrl();
            case ForeignArtifact.Key.SERIES -> CATALOG_NONE.equals(seriesCatalog()) ? dryInstance() : ToolApi.baseUrl();
            default -> ToolApi.baseUrl();
        };
    }

    private static boolean isCaseStudy(ForeignArtifact.Key key) {
        return ForeignArtifact.Key.EXAMPLE.equals(key.kind()) && TestSchema.isCaseStudy(key.id());
    }

    /**
     * Translates the input into the target by the instance and judges the artifact. A translation
     * with a Failure record is the row {@code refused} at level 0, whatever the tool handed over
     * beside the refusal (as the FIBEN run reads a cell); otherwise the artifact is judged as a
     * foreign artifact of producer {@code tool} is.
     */
    static ForeignVerdict judge(ExperimentInput input, int target) throws Exception {
        // A series point carries the catalog of its walk, as the .NET half's point does, so that
        // the summary pairs the row with the point of the same walk.
        boolean series = ForeignArtifact.Key.SERIES.equals(input.key().kind());
        String rowId = input.rowId(target) + (series ? "-" + seriesCatalog() : "");
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

        boolean dry = conversion.catalogState() == CATALOG_NOT_CONFIGURED;
        if (series && dry != CATALOG_NONE.equals(seriesCatalog())) {
            throw new IllegalStateException(rowId + " belongs to the walk with catalog " + seriesCatalog() + ", and the instance that translated it "
                    + (dry ? "has no catalog" : "has one") + "; the row would stand for the other walk.");
        }

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
            ForeignVerdict.write(Path.of(results), ROWS_FILE_NAME, VERDICTS.values(), KEPT);
        }
    }
}
