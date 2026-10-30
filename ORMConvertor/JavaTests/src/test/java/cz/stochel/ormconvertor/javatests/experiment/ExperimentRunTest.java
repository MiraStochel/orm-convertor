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
import java.io.UncheckedIOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
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
 *
 * <p>A point of the limits run (decisions 182, 190 and 195) is carried as the .NET half carries
 * its own: a point whose row the file already holds is not judged again, so that a half stopped
 * from outside continues where it was, and a row still {@code in progress} from an earlier run is
 * the point that run died on ({@code crashed}); a point whose translation into the target the
 * .NET half could not finish ({@code too slow}, {@code crashed}, {@code hung}) is not sent to the
 * instance and its row carries that outcome; the instance has the bound of S3 and a reserve to
 * answer ({@code too slow}), a point has thirty minutes ({@code hung}, the thread judging it
 * abandoned), and an instance that gives no answer is {@code crashed} and waited for before the
 * next point.
 */
@Tag("integration")
class ExperimentRunTest {

    private static final String NONE = "";
    static final String ROWS_FILE_NAME = "java-tool-rows.tsv";
    /** The rows of the .NET half of the limits run, in the same directory. */
    static final String POINTS_FILE_NAME = "series-points.tsv";
    private static final String PRODUCER = "tool";
    private static final Map<String, ForeignVerdict> VERDICTS = new TreeMap<>();

    /** Rows of the file this run does not write again: the series points of the other walk, the points already judged, and the points this run does not send (decisions 182, 195). */
    private static final Map<String, String> KEPT = new TreeMap<>();

    /** Points whose judging was abandoned after the bound of a point; a verdict the abandoned thread reports later is ignored. */
    private static final Set<String> ABANDONED = new HashSet<>();

    /** The bound of one translation by the instance: the 30 s of S3 and a reserve, as the .NET half's child waits (decision 190). */
    static final Duration TRANSLATION_BOUND = Duration.ofSeconds(35);

    /** The bound of one point of the limits run (decision 190). */
    static final Duration POINT_BOUND = Duration.ofMinutes(30);

    /** The outcomes of the .NET half's row of a Java target that say its translation did not finish; such a point is not sent to the instance (decision 195). */
    static final Set<String> NOT_FINISHED_IN_DOTNET = Set.of(ForeignVerdict.TOO_SLOW, ForeignVerdict.CRASHED, ForeignVerdict.HUNG);

    /** Judges a series point on a thread of its own, so that a point over the bound can be abandoned; daemon threads, so that an abandoned one does not keep the JVM alive. */
    private static final ExecutorService POINTS = Executors.newCachedThreadPool(runnable -> {
        Thread thread = new Thread(runnable, "series-point");
        thread.setDaemon(true);
        return thread;
    });

    private static Plan plan;

    static Stream<Arguments> translations() {
        if (results() == null) {
            return Stream.of(Arguments.of(NONE, Orm.HIBERNATE));
        }

        List<Case> cases = plan().cases();
        return cases.isEmpty()
                ? Stream.of(Arguments.of(NONE, Orm.HIBERNATE))
                : cases.stream().map(judged -> Arguments.of(judged.input().directory().toString(), judged.target()));
    }

    @BeforeAll
    static void prepare() throws Exception {
        if (results() != null) {
            Plan planned = plan();
            KEPT.putAll(planned.kept());
            Set<String> instances = new TreeSet<>();
            for (Case judged : planned.cases()) {
                instances.add(instanceOf(judged.input().key()));
            }
            for (String instance : instances) {
                ToolApi.awaitReady(instance);
            }
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
        ForeignVerdict verdict = isSeries(input) ? judgeBounded(input, target) : judge(input, target);
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

    static final String CATALOG_FIXTURE = "fixture";
    static final String CATALOG_NONE = "none";

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

    private static boolean isSeries(ExperimentInput input) {
        return ForeignArtifact.Key.SERIES.equals(input.key().kind());
    }

    /** The identifier of the row: a series point carries the catalog of its walk, as the .NET half's point does, so that the summary pairs the row with the point of the same walk. */
    static String rowIdOf(ExperimentInput input, int target, String catalog) {
        return input.rowId(target) + (isSeries(input) ? "-" + catalog : "");
    }

    /** One translation this run judges. */
    record Case(ExperimentInput input, int target) {
    }

    /** What this run judges and what it keeps as it stands: the rows of the other walk, the points already judged, and the points it does not send (decisions 182, 195). */
    record Plan(List<Case> cases, Map<String, String> kept) {
    }

    private static synchronized Plan plan() {
        if (plan == null) {
            String results = results();
            try {
                plan = plan(ExperimentInput.discover(inputsDirectory(results), TestDatabase.schemaName()), kinds(), CaseStudyDatabase.isConfigured(),
                        Path.of(results, ROWS_FILE_NAME), Path.of(results, POINTS_FILE_NAME), seriesCatalog());
            } catch (IOException e) {
                throw new UncheckedIOException("The rows of an earlier run in " + results + " could not be read.", e);
            }
        }
        return plan;
    }

    /**
     * The plan over the inputs: every input of a kind this environment translates, into the three
     * Java targets, minus what the file of rows and the .NET half's points settle for a series
     * point - a row of the other walk stays as it is; a row of this walk that is finished stays
     * and its point is not judged again; one still {@code in progress} is the point the earlier run
     * died on, {@code crashed} at the level it had reached; a point whose .NET row into the same
     * target says its translation did not finish is not sent to the instance and its row says so
     * with the .NET half's outcome.
     */
    static Plan plan(List<ExperimentInput> inputs, Set<String> kinds, boolean caseStudiesConfigured, Path rowsFile, Path pointsFile, String catalog) throws IOException {
        Map<String, String> kept = new TreeMap<>();
        Map<String, String> earlier = seriesRows(rowsFile);
        Map<String, String[]> points = dotnetRows(pointsFile);

        String other = "-" + (CATALOG_NONE.equals(catalog) ? CATALOG_FIXTURE : CATALOG_NONE);
        for (Map.Entry<String, String> row : earlier.entrySet()) {
            if (row.getKey().endsWith(other)) {
                kept.put(row.getKey(), row.getValue());
            }
        }

        List<Case> cases = new ArrayList<>();
        for (ExperimentInput input : inputs) {
            if (!kinds.contains(input.key().kind())) {
                continue;
            }
            // A case study needs the instance over the case-study database (decision 176); without
            // it the input is left out, as an LDBC key is without the LDBC instance.
            if (isCaseStudy(input.key()) && !caseStudiesConfigured) {
                continue;
            }
            for (int target : new int[] {Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS}) {
                if (!isSeries(input)) {
                    cases.add(new Case(input, target));
                    continue;
                }

                String rowId = rowIdOf(input, target, catalog);
                String done = earlier.get(rowId);
                if (done != null) {
                    ForeignVerdict verdict = ForeignVerdict.parse(done);
                    kept.put(rowId, verdict.isInProgress()
                            ? verdict.stoppedAs(ForeignVerdict.CRASHED, "the earlier run of the Java half ended on this point at level " + verdict.reached() + " (decision 195)").row()
                            : done);
                    continue;
                }

                String[] dotnet = points.get(rowId);
                if (dotnet != null && NOT_FINISHED_IN_DOTNET.contains(dotnet[0])) {
                    kept.put(rowId, ForeignVerdict.stopped(rowId, input.key().toString(), Orm.nameOf(input.source()), Orm.nameOf(target), PRODUCER, dotnet[0],
                            "not sent to the instance: the .NET half's translation into this target ended as " + dotnet[0] + (dotnet[1].isEmpty() ? "" : " (" + dotnet[1] + ")") + " (decision 195)", "{}").row());
                    continue;
                }

                cases.add(new Case(input, target));
            }
        }

        return new Plan(cases, kept);
    }

    /** The series rows of the file of rows by their artifact, verbatim; every other row of the file is this run's to write again. */
    private static Map<String, String> seriesRows(Path file) throws IOException {
        Map<String, String> rows = new TreeMap<>();
        if (!Files.exists(file)) {
            return rows;
        }

        List<String> lines = Files.readAllLines(file);
        for (String line : lines.subList(Math.min(1, lines.size()), lines.size())) {
            String artifact = line.split("\t", 2)[0];
            if (artifact.startsWith(ForeignArtifact.Key.SERIES + "-")) {
                rows.put(artifact, line);
            }
        }
        return rows;
    }

    /** The .NET half's points by their artifact: the outcome and the detail of each, read by the header. */
    private static Map<String, String[]> dotnetRows(Path file) throws IOException {
        Map<String, String[]> rows = new TreeMap<>();
        if (!Files.exists(file)) {
            return rows;
        }

        List<String> lines = Files.readAllLines(file);
        if (lines.isEmpty()) {
            return rows;
        }

        List<String> header = Arrays.asList(lines.get(0).split("\t", -1));
        int artifact = header.indexOf("artifact");
        int outcome = header.indexOf("outcome");
        int detail = header.indexOf("detail");
        if (artifact < 0 || outcome < 0 || detail < 0) {
            throw new IllegalStateException(file + " has no columns artifact, outcome and detail; it is not the file of the .NET half's points.");
        }

        for (String line : lines.subList(1, lines.size())) {
            String[] columns = line.split("\t", -1);
            if (columns.length > Math.max(outcome, detail)) {
                rows.put(columns[artifact], new String[] {columns[outcome], columns[detail]});
            }
        }
        return rows;
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
     * Judges a series point within the bound of a point (decision 190): a point still in progress
     * when it runs out is {@code hung} at the level it had reached, the thread judging it is
     * abandoned - a thread in a JVM cannot be stopped, as the .NET half says of its own -, and the
     * run goes on with the next point.
     */
    static ForeignVerdict judgeBounded(ExperimentInput input, int target) throws Exception {
        String rowId = rowIdOf(input, target, seriesCatalog());
        Future<ForeignVerdict> judging = POINTS.submit(() -> judge(input, target));
        try {
            return judging.get(POINT_BOUND.toMillis(), TimeUnit.MILLISECONDS);
        } catch (TimeoutException e) {
            judging.cancel(true);
            return abandon(rowId, input, target);
        } catch (ExecutionException e) {
            if (e.getCause() instanceof Exception cause) {
                throw cause;
            }
            throw e;
        }
    }

    private static synchronized ForeignVerdict abandon(String rowId, ExperimentInput input, int target) {
        String detail = "still in progress after " + POINT_BOUND.toMinutes() + " minutes, the bound of a point (decision 190); the thread judging it was abandoned";
        ForeignVerdict progress = VERDICTS.remove(rowId);
        ForeignVerdict hung = progress != null
                ? progress.stoppedAs(ForeignVerdict.HUNG, detail)
                : ForeignVerdict.stopped(rowId, input.key().toString(), Orm.nameOf(input.source()), Orm.nameOf(target), PRODUCER, ForeignVerdict.HUNG, detail, "{}");
        ABANDONED.add(rowId);
        KEPT.put(rowId, hung.row());
        write();
        return hung;
    }

    /**
     * Translates the input into the target by the instance and judges the artifact. A translation
     * with a Failure record is the row {@code refused} at level 0, whatever the tool handed over
     * beside the refusal (as the FIBEN run reads a cell); otherwise the artifact is judged as a
     * foreign artifact of producer {@code tool} is. A series point waits for the instance the bound
     * of S3 and a reserve ({@code too slow}), and an instance that gives no answer is
     * {@code crashed} and waited for before the next point (decision 195).
     */
    static ForeignVerdict judge(ExperimentInput input, int target) throws Exception {
        boolean series = isSeries(input);
        String rowId = rowIdOf(input, target, seriesCatalog());
        String answers = input.key().toString();
        String source = Orm.nameOf(input.source());
        String targetName = Orm.nameOf(target);
        String instance = instanceOf(input.key());

        ToolResponse answer;
        try {
            answer = series
                    ? ToolApi.convert(instance, input.source(), target, input.units(), TRANSLATION_BOUND)
                    : ToolApi.convert(instance, input.source(), target, input.units());
        } catch (ToolApi.NoAnswer e) {
            if (!series) {
                throw e;
            }
            ForeignVerdict stopped = e.timedOut()
                    ? ForeignVerdict.stopped(rowId, answers, source, targetName, PRODUCER, ForeignVerdict.TOO_SLOW,
                            "the instance did not answer within " + TRANSLATION_BOUND.toSeconds() + " s, the bound of S3 and a reserve (decision 190); the translation was left to it", "{}")
                    : ForeignVerdict.stopped(rowId, answers, source, targetName, PRODUCER, ForeignVerdict.CRASHED,
                            "the instance gave no answer: " + e.getMessage() + "; the suite waited for it before the next point (decision 195)", "{}");
            record(stopped);
            if (!e.timedOut()) {
                ToolApi.awaitReady(instance);
            }
            return stopped;
        }

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
        if (!series) {
            return ForeignArtifactRun.run(artifact, ExperimentRunTest::record);
        }

        // The path of a series point that throws leaves a row behind - the levels it had reached
        // and what threw -, so that a run continued later does not stand on it again.
        try {
            return ForeignArtifactRun.run(artifact, ExperimentRunTest::record);
        } catch (Exception e) {
            threw(rowId, new ForeignVerdict(rowId, answers, source, targetName, PRODUCER, artifact.language(), metadata), e);
            throw e;
        }
    }

    private static synchronized void threw(String rowId, ForeignVerdict fresh, Exception e) {
        ForeignVerdict progress = VERDICTS.get(rowId);
        record((progress != null ? progress : fresh).stoppedAs(ForeignVerdict.THREW, e.toString()));
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
        if (ABANDONED.contains(verdict.artifact())) {
            return;
        }

        VERDICTS.put(verdict.artifact(), verdict);
        write();
    }

    private static void write() {
        String results = results();
        if (results != null) {
            ForeignVerdict.write(Path.of(results), ROWS_FILE_NAME, VERDICTS.values(), KEPT);
        }
    }
}
