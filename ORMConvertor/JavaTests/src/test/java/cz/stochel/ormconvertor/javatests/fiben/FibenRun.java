package cz.stochel.ormconvertor.javatests.fiben;

import cz.stochel.ormconvertor.javatests.differential.ResultRow;
import cz.stochel.ormconvertor.javatests.ldbc.PreparedJavaQuery;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.lang.reflect.InvocationTargetException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.ResultSet;
import java.sql.ResultSetMetaData;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Duration;
import java.time.ZoneOffset;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;

/**
 * The on-demand run of the FIBEN catalog in the Java suite (decision 149) - the counterpart
 * of {@code FibenRun.cs} for Hibernate, EclipseLink and MyBatis. Every query of the catalog is
 * translated from Dapper into the three targets by an instance whose catalog is the Fiben
 * database, with only the entities of the tables the query reads; the artifacts are compiled,
 * each is run once and its rows put into the canonical form of decision 089 and compared with
 * the source - the catalog's own text, run here over JDBC, because this suite owns no Dapper -
 * and the source is compared with the judge where the database holds one; then every cell is
 * measured by the policy of decision 145, whose values the .NET suite holds equal to its own.
 *
 * <p>The run happens once per test run, for the first test that asks for it, and writes its
 * files into the directory the environment names.
 */
public final class FibenRun {

    /** The frameworks whose artifacts this suite runs. */
    public static final int[] FRAMEWORKS = {Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS};

    /** The policy of decisions 145 and 165, as the .NET BenchmarkPolicy states it; FibenRunTest.cs holds the values equal. */
    public static final int WARMUP_ITERATIONS = 1;
    public static final int MIN_ITERATIONS = 3;
    public static final double TARGET_MILLISECONDS = 1000;

    /** The limit of one execution in seconds (decision 146), the same as the LDBC judge's and the Advisor's. */
    public static final int QUERY_TIMEOUT_SECONDS = 600;

    /** How many rows of a result the judge keeps as rows; beyond that its digest alone. */
    public static final int ROWS_KEPT_BY_THE_JUDGE = 1000;

    /** The keys of the catalog, stated without the instance so that the cases can be counted where nothing is configured. */
    public static final int QUERIES = 237;

    private static FibenRun current;
    private static Exception failure;

    private final String unavailable;
    private final Map<String, Cell> cells = new HashMap<>();
    private final Map<String, Source> sources = new LinkedHashMap<>();
    private final Map<String, String> judgeDigests = new HashMap<>();
    private final Map<String, List<String>> judgeRows = new HashMap<>();

    private FibenCatalog catalog;
    private String dataSet;
    private Server server;
    private boolean hasJudge;
    private Duration elapsed = Duration.ZERO;
    private List<FibenCatalog.Query> queries = List.of();

    private FibenRun(String unavailable) {
        this.unavailable = unavailable;
    }

    /** The run of this test run, made by the first test that asks for it; a run that failed fails every test after it the same way. */
    public static synchronized FibenRun current() throws Exception {
        if (failure != null) {
            throw failure;
        }

        if (current == null) {
            try {
                current = run();
            } catch (Exception e) {
                failure = e;
                throw e;
            }
        }
        return current;
    }

    public void skipIfUnavailable() {
        if (unavailable != null) {
            FibenDatabase.unavailable(unavailable);
        }
    }

    public void skipIfNotCovered(String key) {
        if (!covers(key)) {
            org.junit.jupiter.api.Assumptions.abort("The run was limited to " + FibenDatabase.keys() + " and did not cover " + key + ".");
        }
    }

    public boolean covers(String key) {
        return sources.containsKey(key);
    }

    public FibenCatalog catalog() {
        return catalog;
    }

    public String dataSet() {
        return dataSet;
    }

    public boolean hasJudge() {
        return hasJudge;
    }

    public Duration elapsed() {
        return elapsed;
    }

    public Cell cell(String key, int framework) {
        return cells.get(key + "|" + framework);
    }

    public Source source(String key) {
        return sources.get(key);
    }

    /** The digest of a result in the canonical form, as the judge's line at position 0 writes it; the same in the .NET suite. */
    public static String digest(List<String> rows) {
        try {
            byte[] hash = MessageDigest.getInstance("SHA-256").digest(String.join("\n", rows).getBytes(StandardCharsets.UTF_8));
            StringBuilder hex = new StringBuilder();
            for (byte b : hash) {
                hex.append(String.format(Locale.ROOT, "%02x", b));
            }
            return "rows=" + rows.size() + " sha256=" + hex;
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    public boolean agreesWithSource(Cell cell) {
        Source source = sources.get(cell.key);
        return source != null && source.rows != null && cell.rows != null && cell.rows.equals(source.rows);
    }

    /**
     * Whether the source agrees with the judge; null where there is no judge, the source has no rows
     * or the catalog marks the query as not translated - the text runs over JDBC here, but the run the
     * judge was made from refused it. A query that returned no rows has an entry with the digest of none;
     * any other query the judge has no entry for was not run when the judge was made, so it agrees with nothing.
     */
    public Boolean agreesWithJudge(String key) {
        Source source = sources.get(key);
        if (!hasJudge || source == null || source.rows == null || catalog.query(key).translation() == FibenCatalog.NOT_TRANSLATED) {
            return null;
        }

        String expected = judgeDigests.get(key);
        if (expected == null) {
            return false;
        }

        List<String> kept = judgeRows.getOrDefault(key, List.of());
        return digest(source.rows).equals(expected) && (kept.isEmpty() || kept.equals(source.rows));
    }

    public String judgeDifference(String key) {
        String expected = judgeDigests.get(key);
        if (expected == null) {
            return "the judge holds no entry for it - the run it was made from did not run it, and a new judge is a decision (149)";
        }

        List<String> rows = sources.get(key).rows;
        List<String> kept = judgeRows.getOrDefault(key, List.of());
        return !kept.isEmpty() || rows.size() <= ROWS_KEPT_BY_THE_JUDGE
                ? difference(kept, rows)
                : "the judge holds " + expected + ", the source returned " + digest(rows);
    }

    public static String difference(List<String> expected, List<String> actual) {
        int index = 0;
        while (index < expected.size() && index < actual.size() && expected.get(index).equals(actual.get(index))) {
            index++;
        }

        return "expected " + expected.size() + " rows, got " + actual.size() + "; row " + (index + 1) + ":"
                + System.lineSeparator() + "  expected " + (index < expected.size() ? expected.get(index) : "(no row)")
                + System.lineSeparator() + "  actual   " + (index < actual.size() ? actual.get(index) : "(no row)");
    }

    /** The lines of the summary: the run as a whole, then per framework, then per query. */
    public List<String> describe() {
        List<String> lines = new ArrayList<>();
        lines.add(String.format(Locale.ROOT,
                "FIBEN run (Java) over %s: %d queries in %d frameworks in %.1f min; judge %s; server %s; measurement %s.",
                dataSet, queries.size(), FRAMEWORKS.length, elapsed.toMillis() / 60000.0,
                hasJudge ? "present" : "absent", server == null ? "not read" : server.describe(),
                measurementLeftOut() == null ? "by the policy of decisions 145 and 165" : measurementLeftOut()));

        long sourceFailures = sources.values().stream().filter(source -> source.rows == null).count();
        lines.add(String.format(Locale.ROOT, "source (the text over JDBC): %d of %d ran, %.1f s of first reads.",
                sources.size() - sourceFailures, sources.size(), sources.values().stream().mapToLong(source -> source.selectNanos).sum() / 1e9));

        for (int framework : FRAMEWORKS) {
            Map<String, Integer> outcomes = new TreeMap<>();
            int ran = 0;
            int agreeing = 0;
            long translated = 0;
            long compiled = 0;
            long selected = 0;
            List<Double> medians = new ArrayList<>();
            for (FibenCatalog.Query query : queries) {
                Cell cell = cell(query.key(), framework);
                outcomes.merge(cell.outcome(), 1, Integer::sum);
                translated += cell.translateNanos;
                compiled += cell.compileNanos;
                selected += cell.selectNanos;
                if (cell.rows != null) {
                    ran++;
                    if (agreesWithSource(cell)) {
                        agreeing++;
                    }
                }
                if (cell.medianMilliseconds != null) {
                    medians.add(cell.medianMilliseconds);
                }
            }

            StringBuilder outcomeText = new StringBuilder();
            outcomes.forEach((outcome, count) -> outcomeText.append(outcomeText.length() == 0 ? "" : ", ").append(count).append(' ').append(outcome));
            String measurement = medians.isEmpty() ? "" : String.format(Locale.ROOT,
                    "; measured %d cells, median of medians %.1f ms, sum of medians %.1f s",
                    medians.size(), median(medians), medians.stream().mapToDouble(Double::doubleValue).sum() / 1000);
            lines.add(String.format(Locale.ROOT,
                    "%s: %s; %d of %d runs agree with the source; translation %.1f s, compilation %.1f s (bootstrap %.1f s of it), first reads %.1f s%s.",
                    Orm.nameOf(framework), outcomeText, agreeing, ran, translated / 1e9, compiled / 1e9,
                    PreparedJavaQuery.bootstrapNanos(framework) / 1e9, selected / 1e9, measurement));
        }

        for (FibenCatalog.Query query : queries) {
            Source source = sources.get(query.key());
            StringBuilder parts = new StringBuilder();
            for (int framework : FRAMEWORKS) {
                Cell cell = cell(query.key(), framework);
                String agreement = cell.rows != null ? (agreesWithSource(cell) ? " =" : " !=") : "";
                String median = cell.medianMilliseconds != null ? String.format(Locale.ROOT, " %.1f ms", cell.medianMilliseconds) : "";
                parts.append(parts.length() == 0 ? "" : "; ").append(Orm.nameOf(framework)).append(' ').append(cell.outcome()).append(agreement).append(median);
            }
            Boolean judged = agreesWithJudge(query.key());
            String judge = !hasJudge ? "" : judged == null ? ", judge -" : judged ? ", judge =" : ", judge !=";
            lines.add(query.key() + " (" + nestingName(query.nesting()) + ", "
                    + (source.rows != null ? source.rows.size() + " rows" : "source failed: " + source.failed)
                    + (source.medianMilliseconds != null ? String.format(Locale.ROOT, ", text %.1f ms", source.medianMilliseconds) : "")
                    + judge + "): " + parts);
        }

        return lines;
    }

    private static String nestingName(int nesting) {
        switch (nesting) {
            case 10: return "NonNested";
            case 20: return "TypeN";
            case 30: return "TypeA";
            case 40: return "TypeJ";
            case 50: return "TypeJA";
            default: return String.valueOf(nesting);
        }
    }

    private static FibenRun run() throws Exception {
        String reason = FibenDatabase.notConfigured();
        if (reason != null) {
            return new FibenRun(reason);
        }

        String jdbcUrl = FibenDatabase.jdbcUrl();
        Connection control;
        String dataSet;
        Server server;
        try {
            control = DriverManager.getConnection(jdbcUrl);
            dataSet = scalar(control, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'fiben.dataset'");
            server = Server.read(control);
        } catch (SQLException e) {
            return new FibenRun("The FIBEN database " + FibenDatabase.JDBC_URL_VARIABLE + " names cannot be reached: " + e.getMessage());
        }

        try (control) {
            FibenRun run = new FibenRun(null);
            run.dataSet = dataSet;
            run.server = server;
            ToolApi.awaitReady(FibenDatabase.apiUrl());
            run.catalog = ToolApi.get(FibenDatabase.apiUrl(), "/fiben", FibenCatalog.class);
            run.execute(control, jdbcUrl);
            return run;
        }
    }

    private void execute(Connection control, String jdbcUrl) throws SQLException {
        long start = System.nanoTime();
        readJudge(control);

        Set<String> keys = FibenDatabase.keys();
        queries = catalog.queries().stream()
                .filter(query -> keys == null || keys.contains(query.key().toLowerCase(Locale.ROOT)))
                .toList();

        for (FibenCatalog.Query query : queries) {
            Source source = new Source();
            sources.put(query.key(), source);
            long started = System.nanoTime();
            try {
                source.rows = canonical(query, text(control, query));
            } catch (Exception e) {
                source.failed = innermost(e).toString();
            }
            source.selectNanos = System.nanoTime() - started;

            Map<Integer, PreparedJavaQuery> prepared = new LinkedHashMap<>();
            try {
                for (int framework : FRAMEWORKS) {
                    Cell cell = new Cell(query.key(), framework);
                    cells.put(query.key() + "|" + framework, cell);

                    started = System.nanoTime();
                    ConversionResponse response;
                    try {
                        ToolResponse answer = ToolApi.convert(FibenDatabase.apiUrl(), Orm.DAPPER, framework, catalog.units(query));
                        response = answer.required();
                    } catch (Throwable e) {
                        cell.translateNanos = System.nanoTime() - started;
                        cell.refused = "the conversion failed: " + innermost(e);
                        continue;
                    }
                    cell.translateNanos = System.nanoTime() - started;

                    if (!response.recordsOf(RecordKind.FAILURE).isEmpty()) {
                        StringBuilder reasons = new StringBuilder();
                        response.recordsOf(RecordKind.FAILURE).forEach(record -> reasons.append(reasons.length() == 0 ? "" : "; ").append(record.reason()));
                        cell.refused = reasons.toString();
                        continue;
                    }
                    cell.fellBack = !response.recordsOf(RecordKind.FALLBACK).isEmpty();

                    started = System.nanoTime();
                    try {
                        prepared.put(framework, PreparedJavaQuery.prepare(query.key(), framework, response, null, jdbcUrl));
                    } catch (Throwable e) {
                        cell.compileNanos = System.nanoTime() - started;
                        cell.unprepared = innermost(e).toString();
                        continue;
                    }
                    cell.compileNanos = System.nanoTime() - started;

                    started = System.nanoTime();
                    try {
                        List<List<Object>> rows = prepared.get(framework).run(query.key(), new Object[0], query.columns());
                        cell.selectNanos = System.nanoTime() - started;
                        cell.rows = canonical(query, rows);
                    } catch (Throwable e) {
                        cell.selectNanos = System.nanoTime() - started;
                        cell.failed = innermost(e).toString();
                    }
                }

                if (measurementLeftOut() == null) {
                    if (source.rows != null) {
                        measure(source, () -> text(control, query));
                    }
                    for (Map.Entry<Integer, PreparedJavaQuery> entry : prepared.entrySet()) {
                        Cell cell = cell(query.key(), entry.getKey());
                        if (cell.failed == null) {
                            measure(cell, () -> entry.getValue().run(query.key(), new Object[0], query.columns()));
                        }
                    }
                }
            } finally {
                for (PreparedJavaQuery artifact : prepared.values()) {
                    artifact.close();
                }
                // The entities of a query are the tables it reads, so the compiled domains differ
                // from query to query; closed here rather than kept for the whole catalog.
                PreparedJavaQuery.closeAll();
            }
        }

        elapsed = Duration.ofNanos(System.nanoTime() - start);
        writeResults();
    }

    private interface Execution {
        void run() throws Exception;
    }

    /**
     * The policy of decision 165 over one cell, as the .NET run applies it: the read that
     * produced the rows was the cell's first, cold execution and is its warm-up, and the
     * policy's further warm-ups, if any, follow it.
     */
    private static void measure(Measured measured, Execution execution) {
        try {
            for (int warmup = 1; warmup < WARMUP_ITERATIONS; warmup++) {
                execution.run();
            }

            List<Double> times = new ArrayList<>();
            double total = 0;
            while (times.size() < MIN_ITERATIONS || total < TARGET_MILLISECONDS) {
                double milliseconds = timeOne(execution);
                times.add(milliseconds);
                total += milliseconds;
            }

            measured.setMeasurement(median(times), WARMUP_ITERATIONS, times.size());
        } catch (Exception e) {
            measured.setMeasurementFailed(innermost(e).toString());
        }
    }

    private static double timeOne(Execution execution) throws Exception {
        long started = System.nanoTime();
        execution.run();
        return (System.nanoTime() - started) / 1e6;
    }

    private static double median(List<Double> values) {
        List<Double> sorted = new ArrayList<>(values);
        sorted.sort(Double::compareTo);
        int count = sorted.size();
        return count == 0 ? 0 : count % 2 == 1 ? sorted.get(count / 2) : (sorted.get(count / 2 - 1) + sorted.get(count / 2)) / 2;
    }

    /** The catalog's own text over JDBC: the source of the comparison, with the columns in the order the catalog states. */
    private static List<List<Object>> text(Connection control, FibenCatalog.Query query) throws SQLException {
        List<List<Object>> rows = new ArrayList<>();
        try (Statement statement = control.createStatement(); ResultSet result = statement.executeQuery(query.sql())) {
            ResultSetMetaData meta = result.getMetaData();
            if (meta.getColumnCount() != query.columns().size()) {
                throw new IllegalStateException(query.key() + ": the text returns " + meta.getColumnCount() + " columns, the catalog states " + query.columns().size() + ".");
            }
            for (int index = 1; index <= meta.getColumnCount(); index++) {
                if (!meta.getColumnLabel(index).equalsIgnoreCase(query.columns().get(index - 1))) {
                    throw new IllegalStateException(query.key() + ": column " + index + " is " + meta.getColumnLabel(index) + ", the catalog states " + query.columns().get(index - 1) + ".");
                }
            }
            while (result.next()) {
                List<Object> row = new ArrayList<>(meta.getColumnCount());
                for (int index = 1; index <= meta.getColumnCount(); index++) {
                    row.add(result.getObject(index));
                }
                rows.add(row);
            }
        }
        return rows;
    }

    private static List<String> canonical(FibenCatalog.Query query, List<List<Object>> rows) {
        List<String> rendered = new ArrayList<>(rows.size());
        for (List<Object> row : rows) {
            rendered.add(ResultRow.render(row, ResultRow.Settings.defaults()));
        }
        return query.ordered() ? rendered : ResultRow.sorted(rendered);
    }

    private void readJudge(Connection control) throws SQLException {
        if (scalar(control, "SELECT CAST(OBJECT_ID(N'[" + FibenDatabase.SCHEMA + "].[Judge]', N'U') AS NVARCHAR(30))") == null) {
            return;
        }

        try (Statement statement = control.createStatement();
             ResultSet result = statement.executeQuery("SELECT [QueryKey], [Position], [Rendered] FROM [" + FibenDatabase.SCHEMA + "].[Judge] ORDER BY [QueryKey], [Position];")) {
            while (result.next()) {
                String key = result.getString(1);
                if (result.getInt(2) == 0) {
                    judgeDigests.put(key, result.getNString(3));
                } else {
                    judgeRows.computeIfAbsent(key, k -> new ArrayList<>()).add(result.getNString(3));
                }
            }
        }
        hasJudge = true;
    }

    private void writeResults() {
        String directory = FibenDatabase.resultsDirectory();
        if (directory == null) {
            return;
        }

        try {
            Path root = Files.createDirectories(Path.of(directory));
            StringBuilder cellsFile = new StringBuilder();
            cellsFile.append("key\tnesting\tframework\toutcome\tfellBack\ttranslateMs\tcompileMs\tfirstReadMs\trows\tagreesWithSource\tagreesWithJudge\tmedianMs\twarmups\titerations\tdetail\n");
            for (FibenCatalog.Query query : queries) {
                Source source = sources.get(query.key());
                Boolean judged = agreesWithJudge(query.key());
                cellsFile.append(String.join("\t", query.key(), nestingName(query.nesting()), "Text", source.rows != null ? "ran" : "failed", "0",
                        "", "", String.format(Locale.ROOT, "%.0f", source.selectNanos / 1e6),
                        source.rows != null ? String.valueOf(source.rows.size()) : "", "",
                        judged == null ? "" : judged ? "1" : "0",
                        source.medianMilliseconds != null ? String.format(Locale.ROOT, "%.1f", source.medianMilliseconds) : "",
                        source.medianMilliseconds != null ? String.valueOf(source.warmupIterations) : "",
                        source.medianMilliseconds != null ? String.valueOf(source.iterations) : "",
                        clean(source.failed != null ? source.failed : source.measurementFailed != null ? source.measurementFailed : ""))).append('\n');
                for (int framework : FRAMEWORKS) {
                    Cell cell = cell(query.key(), framework);
                    String detail = cell.refused != null ? cell.refused : cell.unprepared != null ? cell.unprepared : cell.failed != null ? cell.failed : cell.measurementFailed != null ? cell.measurementFailed : "";
                    cellsFile.append(String.join("\t", query.key(), nestingName(query.nesting()), Orm.nameOf(framework), cell.outcome(), cell.fellBack ? "1" : "0",
                            String.format(Locale.ROOT, "%.0f", cell.translateNanos / 1e6),
                            String.format(Locale.ROOT, "%.0f", cell.compileNanos / 1e6),
                            String.format(Locale.ROOT, "%.0f", cell.selectNanos / 1e6),
                            cell.rows != null ? String.valueOf(cell.rows.size()) : "",
                            cell.rows != null ? (agreesWithSource(cell) ? "1" : "0") : "",
                            "",
                            cell.medianMilliseconds != null ? String.format(Locale.ROOT, "%.1f", cell.medianMilliseconds) : "",
                            cell.medianMilliseconds != null ? String.valueOf(cell.warmupIterations) : "",
                            cell.medianMilliseconds != null ? String.valueOf(cell.iterations) : "",
                            clean(detail))).append('\n');
                }
            }
            Files.writeString(root.resolve("java-cells.tsv"), cellsFile.toString(), StandardCharsets.UTF_8);

            // The source rows in the form of database/fiben/judge.tsv, so that the two suites' sources can be set beside each other.
            StringBuilder judgeFile = new StringBuilder();
            judgeFile.append("# The rows of every FIBEN query in the canonical form of decision 089, as the catalog's own text returned them over ")
                    .append(dataSet).append(" through JDBC on ")
                    .append(ZonedDateTime.now(ZoneOffset.UTC).format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm 'UTC'"))).append(".\n");
            judgeFile.append("# key<TAB>position<TAB>rendered row; position 0 is the digest of the whole result, the rows follow where there are at most ")
                    .append(ROWS_KEPT_BY_THE_JUDGE).append(". Made by the Java suite's FibenRunTest.\n");
            for (FibenCatalog.Query query : queries) {
                Source source = sources.get(query.key());
                if (source.rows == null) {
                    continue;
                }
                judgeFile.append(query.key()).append("\t0\t").append(digest(source.rows)).append('\n');
                if (source.rows.size() > ROWS_KEPT_BY_THE_JUDGE) {
                    continue;
                }
                for (int position = 0; position < source.rows.size(); position++) {
                    judgeFile.append(query.key()).append('\t').append(position + 1).append('\t').append(source.rows.get(position)).append('\n');
                }
            }
            Files.writeString(root.resolve("java-judge-candidate.tsv"), judgeFile.toString(), StandardCharsets.UTF_8);

            Files.write(root.resolve("java-summary.txt"), describe(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException("The results of the FIBEN run could not be written into " + directory + ".", e);
        }
    }

    private static String clean(String text) {
        return text.replace('\t', ' ').replace("\r", "").replace('\n', ' ');
    }

    /** Why the run leaves its measurement out, or null when it measures (decisions 145, 164 and 166). */
    private String measurementLeftOut() {
        return server == null ? "left out" : Server.measurementLeftOut(FibenDatabase.measures(), server.maxdop(), server.processors());
    }

    /**
     * The settings of the server a run reads before it starts (decision 164), as {@code FibenServer}
     * in {@code FibenRun.cs} reads them: the MAXDOP the Fiben database runs its plans with - its
     * own database-scoped value where that is not zero, the server's otherwise -, the server's
     * memory, and the processors it sees, which need VIEW SERVER STATE and are null without it.
     * The first two decide whether the run measures (decision 166): the measurement mode runs
     * single-threaded plans on processors of the server's own, and the judge's mode, whose plans
     * are single-threaded too, gives the server every processor of the VM.
     */
    record Server(int maxdop, int maxServerMemoryMegabytes, Integer processors) {

        /** The processors of the database in the measurement mode, as {@code FibenServer} states them; FibenRunTest.cs holds the values equal. */
        static final int MEASUREMENT_PROCESSORS = 2;

        static Server read(Connection connection) throws SQLException {
            int maxdop;
            int memory;
            try (Statement statement = connection.createStatement();
                 ResultSet rows = statement.executeQuery(
                         "SELECT COALESCE(NULLIF((SELECT CAST(value AS INT) FROM sys.database_scoped_configurations WHERE name = N'MAXDOP'), 0), "
                         + "(SELECT CAST(value_in_use AS INT) FROM sys.configurations WHERE name = N'max degree of parallelism')), "
                         + "(SELECT CAST(value_in_use AS INT) FROM sys.configurations WHERE name = N'max server memory (MB)')")) {
                rows.next();
                maxdop = rows.getInt(1);
                memory = rows.getInt(2);
            }

            Integer processors;
            try (Statement statement = connection.createStatement();
                 ResultSet rows = statement.executeQuery("SELECT cpu_count FROM sys.dm_os_sys_info")) {
                processors = rows.next() ? rows.getInt(1) : null;
            } catch (SQLException e) {
                processors = null;
            }

            return new Server(maxdop, memory, processors);
        }

        /**
         * Why a run leaves its measurement out, or null when it measures: the environment may
         * leave it out, and a server outside the measurement mode - parallel plans, or processors
         * other than its own two, as in the judge's mode - leaves it out by itself, so that no
         * number comes from it.
         */
        static String measurementLeftOut(boolean configured, int maxdop, Integer processors) {
            if (!configured) {
                return "left out";
            }
            if (maxdop != 1) {
                return "left out: the server runs its plans with MAXDOP " + maxdop + ", not in the measurement mode (decision 164)";
            }
            if (processors == null || processors != MEASUREMENT_PROCESSORS) {
                return "left out: the server has " + (processors == null ? "an unknown number of" : processors.toString())
                        + " processors, not the " + MEASUREMENT_PROCESSORS + " of its own the measurement mode gives it (decision 166)";
            }
            return null;
        }

        String describe() {
            return String.format(Locale.ROOT, "MAXDOP %d, max server memory %d MB, %s processors",
                    maxdop, maxServerMemoryMegabytes, processors == null ? "unknown" : processors.toString());
        }
    }

    private static String scalar(Connection connection, String sql) throws SQLException {
        try (Statement statement = connection.createStatement(); ResultSet rows = statement.executeQuery(sql)) {
            return rows.next() ? rows.getString(1) : null;
        }
    }

    static Throwable innermost(Throwable e) {
        Throwable current = e;
        while (current instanceof InvocationTargetException && current.getCause() != null) {
            current = current.getCause();
        }
        return current;
    }

    /** What a measured thing - a cell or the source - records of its measurement. */
    abstract static class Measured {

        Double medianMilliseconds;
        int warmupIterations;
        int iterations;
        String measurementFailed;

        void setMeasurement(double median, int warmups, int iterations) {
            this.medianMilliseconds = median;
            this.warmupIterations = warmups;
            this.iterations = iterations;
        }

        void setMeasurementFailed(String reason) {
            this.measurementFailed = reason;
        }
    }

    /** The catalog's own text over JDBC. */
    public static final class Source extends Measured {

        public List<String> rows;
        public String failed;
        long selectNanos;
    }

    /** What one query did in one framework. */
    public static final class Cell extends Measured {

        public final String key;
        public final int framework;
        public String refused;
        public boolean fellBack;
        public String unprepared;
        public String failed;
        public List<String> rows;
        long translateNanos;
        long compileNanos;
        long selectNanos;

        Cell(String key, int framework) {
            this.key = key;
            this.framework = framework;
        }

        public String outcome() {
            return refused != null ? "refused" : unprepared != null ? "not compiled" : failed != null ? "failed" : fellBack ? "native SQL" : "translated";
        }

        public boolean ran() {
            return rows != null;
        }

        public String describe() {
            String cell = key + " in " + Orm.nameOf(framework);
            if (refused != null) {
                return cell + ": refused: " + refused;
            }
            if (unprepared != null) {
                return cell + ": the artifact could not be made: " + unprepared;
            }
            if (failed != null) {
                return cell + ": the artifact failed to run: " + failed;
            }
            return cell + ": " + rows.size() + " rows" + (fellBack ? " (native SQL)" : "");
        }
    }
}
