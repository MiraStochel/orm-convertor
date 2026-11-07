package cz.stochel.ormconvertor.javatests.catalog;

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
import java.lang.reflect.Parameter;
import java.lang.reflect.Type;
import java.math.BigDecimal;
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
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.OffsetDateTime;
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
import java.util.function.Function;

/**
 * The on-demand run of a query catalog in the Java suite (decisions 149 and 208) - the
 * counterpart of {@code CatalogRun.cs} for Hibernate, EclipseLink and MyBatis. Every query of
 * the catalog is translated from Dapper into the three targets by an instance whose catalog is
 * the catalog's database, with only the entities of the tables the query reads; the artifacts
 * are compiled, each is run once with the catalog's values and its rows put into the canonical
 * form of decision 089 and compared with the source - the catalog's own text, run here over
 * JDBC with its values declared in front of it, because this suite owns no Dapper - and the
 * source is compared with the judge where the database holds one; then every cell is measured
 * by the policy of decision 145, whose values the .NET suite holds equal to its own.
 *
 * <p>The run happens once per test run and catalog, for the first test that asks for it, and
 * writes its files into the directory the environment names.
 */
public final class CatalogRun {

    /** The frameworks whose artifacts this suite runs. */
    public static final int[] FRAMEWORKS = {Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS};

    /** The policy of decisions 145 and 165, as the .NET BenchmarkPolicy states it; CatalogRunTest.cs holds the values equal. */
    public static final int WARMUP_ITERATIONS = 1;
    public static final int MIN_ITERATIONS = 3;
    public static final double TARGET_MILLISECONDS = 1000;

    /** The limit of one execution in seconds (decision 146), the same as the LDBC judge's and the Advisor's. */
    public static final int QUERY_TIMEOUT_SECONDS = 600;

    /** How many rows of a result the judge keeps as rows; beyond that its digest alone. */
    public static final int ROWS_KEPT_BY_THE_JUDGE = 1000;

    /** What a run is over: the catalog's database, the data set its texts are written over, where the instance serves it and how its queries are read. */
    public record Definition<C>(
            CatalogDatabase database,
            String dataSetProperty,
            String endpoint,
            Class<C> catalogType,
            Function<C, String> dataSetOf,
            Function<C, List<CatalogQuery>> queriesOf) {
    }

    private static final Map<String, CatalogRun> current = new HashMap<>();
    private static final Map<String, Exception> failures = new HashMap<>();

    private final CatalogDatabase database;
    private final String unavailable;
    private final Map<String, Cell> cells = new HashMap<>();
    private final Map<String, Source> sources = new LinkedHashMap<>();
    private final Map<String, String> judgeDigests = new HashMap<>();
    private final Map<String, List<String>> judgeRows = new HashMap<>();
    private final Map<String, CatalogQuery> byKey = new HashMap<>();

    private Object catalog;
    private String dataSet;
    private Server server;
    private boolean hasJudge;
    private Duration elapsed = Duration.ZERO;
    private List<CatalogQuery> queries = List.of();

    private CatalogRun(CatalogDatabase database, String unavailable) {
        this.database = database;
        this.unavailable = unavailable;
    }

    /** The run of this test run over the catalog, made by the first test that asks for it; a run that failed fails every test after it the same way. */
    public static synchronized <C> CatalogRun of(Definition<C> definition) throws Exception {
        String name = definition.database().name();
        if (failures.containsKey(name)) {
            throw failures.get(name);
        }

        if (!current.containsKey(name)) {
            try {
                current.put(name, run(definition));
            } catch (Exception e) {
                failures.put(name, e);
                throw e;
            }
        }
        return current.get(name);
    }

    public CatalogDatabase database() {
        return database;
    }

    public void skipIfUnavailable() {
        if (unavailable != null) {
            database.unavailable(unavailable);
        }
    }

    public void skipIfNotCovered(String key) {
        if (!covers(key)) {
            org.junit.jupiter.api.Assumptions.abort("The run was limited to " + database.keys() + " and did not cover " + key + ".");
        }
    }

    public boolean covers(String key) {
        return sources.containsKey(key);
    }

    /** The catalog as the instance served it, in the type the definition names. */
    @SuppressWarnings("unchecked")
    public <C> C catalog() {
        return (C) catalog;
    }

    public CatalogQuery query(String key) {
        CatalogQuery query = byKey.get(key);
        if (query == null) {
            throw new IllegalStateException("The catalog has no query " + key + ".");
        }
        return query;
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
     * or the catalog marks the query as not translated - the run the judge was made from refused it.
     * A query that returned no rows has an entry with the digest of none; any other query the judge
     * has no entry for was not run when the judge was made, so it agrees with nothing.
     */
    public Boolean agreesWithJudge(String key) {
        Source source = sources.get(key);
        if (!hasJudge || source == null || source.rows == null || !query(key).translated()) {
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
            return "the judge holds no entry for it - the run it was made from did not run it, and a new judge is a decision (" + database.decision() + ")";
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
                "%s run (Java) over %s: %d queries in %d frameworks in %.1f min; judge %s; server %s; measurement %s.",
                database.name(), dataSet, queries.size(), FRAMEWORKS.length, elapsed.toMillis() / 60000.0,
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
            for (CatalogQuery query : queries) {
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

        for (CatalogQuery query : queries) {
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
            lines.add(query.key() + " (" + query.group() + ", "
                    + (source.rows != null ? source.rows.size() + " rows" : source.failed != null ? "source failed: " + source.failed : "source not run")
                    + (source.medianMilliseconds != null ? String.format(Locale.ROOT, ", text %.1f ms", source.medianMilliseconds) : "")
                    + judge + "): " + parts);
        }

        return lines;
    }

    private static <C> CatalogRun run(Definition<C> definition) throws Exception {
        CatalogDatabase database = definition.database();
        String reason = database.notConfigured();
        if (reason != null) {
            return new CatalogRun(database, reason);
        }

        String jdbcUrl = database.jdbcUrl();
        Connection control;
        String dataSet;
        Server server;
        try {
            control = DriverManager.getConnection(jdbcUrl);
            dataSet = scalar(control, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'" + definition.dataSetProperty() + "'");
            server = Server.read(control);
        } catch (SQLException e) {
            return new CatalogRun(database, "The " + database.name() + " database " + database.jdbcUrlVariable() + " names cannot be reached: " + e.getMessage());
        }

        try (control) {
            CatalogRun run = new CatalogRun(database, null);
            run.dataSet = dataSet;
            run.server = server;
            ToolApi.awaitReady(database.apiUrl());
            C catalog = ToolApi.get(database.apiUrl(), definition.endpoint(), definition.catalogType());
            run.catalog = catalog;
            List<CatalogQuery> all = definition.queriesOf().apply(catalog);
            all.forEach(query -> run.byKey.put(query.key(), query));
            run.execute(control, jdbcUrl, all);
            return run;
        }
    }

    private void execute(Connection control, String jdbcUrl, List<CatalogQuery> all) throws SQLException {
        long start = System.nanoTime();
        readJudge(control);

        Set<String> keys = database.keys();
        queries = all.stream()
                .filter(query -> keys == null || keys.contains(query.key().toLowerCase(Locale.ROOT)))
                .toList();

        for (CatalogQuery query : queries) {
            Source source = new Source();
            sources.put(query.key(), source);
            if (query.translated()) {
                long started = System.nanoTime();
                try {
                    source.rows = canonical(query, text(control, query));
                } catch (Exception e) {
                    source.failed = innermost(e).toString();
                }
                source.selectNanos = System.nanoTime() - started;
            }

            Map<Integer, PreparedJavaQuery> prepared = new LinkedHashMap<>();
            try {
                for (int framework : FRAMEWORKS) {
                    Cell cell = new Cell(query.key(), framework);
                    cells.put(query.key() + "|" + framework, cell);

                    long started = System.nanoTime();
                    ConversionResponse response;
                    try {
                        ToolResponse answer = ToolApi.convert(database.apiUrl(), Orm.DAPPER, framework, query.units());
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
                        Object[] arguments = argumentsFor(prepared.get(framework), query);
                        List<List<Object>> rows = prepared.get(framework).run(query.key(), arguments, query.columns());
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
                            Object[] arguments = argumentsFor(entry.getValue(), query);
                            measure(cell, () -> entry.getValue().run(query.key(), arguments, query.columns()));
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

    /**
     * The arguments of a read, in the order of the parameters of the generated method: the value
     * the catalog states for each name, converted to the type the method declares. The name is
     * found without case and underscores - the catalog's UserId or p_UserId is the method's
     * userId or pUserId. A parameter the catalog values under no name fails the cell with its name.
     */
    static Object[] argumentsFor(PreparedJavaQuery artifact, CatalogQuery query) {
        List<String> names = artifact.parameterNames();
        List<Type> types = artifact.parameterTypes();
        Object[] arguments = new Object[names.size()];
        for (int index = 0; index < names.size(); index++) {
            String name = names.get(index);
            CatalogQuery.Argument argument = query.arguments().stream()
                    .filter(candidate -> sameName(candidate.name(), name))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException(query.key() + ": the generated method takes the parameter " + name + ", which the catalog does not value."));
            arguments[index] = argument(types.get(index), argument.value());
        }
        return arguments;
    }

    private static boolean sameName(String first, String second) {
        return first.replace("_", "").equalsIgnoreCase(second.replace("_", ""));
    }

    static Object argument(Type type, String text) {
        if (type == String.class) {
            return text;
        }
        if (type == int.class || type == Integer.class) {
            return Integer.parseInt(text.trim());
        }
        if (type == long.class || type == Long.class) {
            return Long.parseLong(text.trim());
        }
        if (type == short.class || type == Short.class) {
            return Short.parseShort(text.trim());
        }
        if (type == byte.class || type == Byte.class) {
            return Byte.parseByte(text.trim());
        }
        if (type == double.class || type == Double.class) {
            return Double.parseDouble(text.trim());
        }
        if (type == float.class || type == Float.class) {
            return Float.parseFloat(text.trim());
        }
        if (type == BigDecimal.class) {
            return new BigDecimal(text.trim());
        }
        if (type == boolean.class || type == Boolean.class) {
            return text.trim().equals("1") || text.trim().equalsIgnoreCase("true");
        }
        if (type == LocalDate.class) {
            return LocalDate.parse(text.trim().length() > 10 ? text.trim().substring(0, 10) : text.trim());
        }
        if (type == LocalDateTime.class) {
            return text.trim().length() > 10 ? LocalDateTime.parse(text.trim().replace(' ', 'T')) : LocalDate.parse(text.trim()).atStartOfDay();
        }
        if (type == OffsetDateTime.class) {
            LocalDateTime moment = text.trim().length() > 10 ? LocalDateTime.parse(text.trim().replace(' ', 'T')) : LocalDate.parse(text.trim()).atStartOfDay();
            return moment.atOffset(ZoneOffset.UTC);
        }
        throw new IllegalStateException("A parameter of type " + type.getTypeName() + " has no conversion from the catalog's text.");
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

    /**
     * The catalog's own text over JDBC: the source of the comparison, with the columns in the
     * order the catalog states, and its parameters declared in front of it with the catalog's
     * values - the one way a text with {@code @name} variables runs unchanged over JDBC.
     */
    private static List<List<Object>> text(Connection control, CatalogQuery query) throws SQLException {
        StringBuilder batch = new StringBuilder();
        for (CatalogQuery.Argument argument : query.arguments()) {
            batch.append("DECLARE @").append(argument.name()).append(' ').append(argument.sqlType()).append(" = ").append(argument.literal()).append(";\n");
        }
        batch.append(query.sql());

        List<List<Object>> rows = new ArrayList<>();
        try (Statement statement = control.createStatement(); ResultSet result = statement.executeQuery(batch.toString())) {
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

    /** The rows in the canonical form of decision 170: in the order of the query's key, the rows of one key value as a set. */
    private static List<String> canonical(CatalogQuery query, List<List<Object>> rows) {
        return ResultRow.canonical(rows, ResultRow.Settings.defaults(), query.columns(), query.orderingKey());
    }

    private void readJudge(Connection control) throws SQLException {
        if (scalar(control, "SELECT CAST(OBJECT_ID(N'[" + CatalogDatabase.SCHEMA + "].[Judge]', N'U') AS NVARCHAR(30))") == null) {
            return;
        }

        try (Statement statement = control.createStatement();
             ResultSet result = statement.executeQuery("SELECT [QueryKey], [Position], [Rendered] FROM [" + CatalogDatabase.SCHEMA + "].[Judge] ORDER BY [QueryKey], [Position];")) {
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
        String directory = database.resultsDirectory();
        if (directory == null) {
            return;
        }

        try {
            Path root = Files.createDirectories(Path.of(directory));
            StringBuilder cellsFile = new StringBuilder();
            cellsFile.append("key\tgroup\tframework\toutcome\tfellBack\ttranslateMs\tcompileMs\tfirstReadMs\trows\tagreesWithSource\tagreesWithJudge\tmedianMs\twarmups\titerations\tdetail\n");
            for (CatalogQuery query : queries) {
                Source source = sources.get(query.key());
                Boolean judged = agreesWithJudge(query.key());
                cellsFile.append(String.join("\t", query.key(), query.group(), "Text", source.rows != null ? "ran" : query.translated() ? "failed" : "not run", "0",
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
                    cellsFile.append(String.join("\t", query.key(), query.group(), Orm.nameOf(framework), cell.outcome(), cell.fellBack ? "1" : "0",
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

            // The source rows in the form of judge.tsv, so that the two suites' sources can be set beside each other.
            StringBuilder judgeFile = new StringBuilder();
            judgeFile.append("# The rows of every ").append(database.name()).append(" query in the canonical form of decisions 089 and 170, as the catalog's own text returned them over ")
                    .append(dataSet).append(" through JDBC on ")
                    .append(ZonedDateTime.now(ZoneOffset.UTC).format(DateTimeFormatter.ofPattern("yyyy-MM-dd HH:mm 'UTC'"))).append(".\n");
            judgeFile.append("# key<TAB>position<TAB>rendered row; position 0 is the digest of the whole result, the rows follow where there are at most ")
                    .append(ROWS_KEPT_BY_THE_JUDGE).append(". Made by the Java suite's ").append(database.name()).append(" run.\n");
            for (CatalogQuery query : queries) {
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
            throw new UncheckedIOException("The results of the " + database.name() + " run could not be written into " + directory + ".", e);
        }
    }

    private static String clean(String text) {
        return text.replace('\t', ' ').replace("\r", "").replace('\n', ' ');
    }

    /** Why the run leaves its measurement out, or null when it measures (decisions 145, 164 and 166). */
    private String measurementLeftOut() {
        return server == null ? "left out" : Server.measurementLeftOut(database.measures(), server.maxdop(), server.processors());
    }

    /**
     * The settings of the server a run reads before it starts (decision 164), as {@code CatalogServer}
     * in {@code CatalogRun.cs} reads them: the MAXDOP the catalog's database runs its plans with -
     * its own database-scoped value where that is not zero, the server's otherwise -, the server's
     * memory, and the processors it sees, which need VIEW SERVER STATE and are null without it.
     * The first two decide whether the run measures (decision 166): the measurement mode runs
     * single-threaded plans on processors of the server's own, and the judge's mode, whose plans
     * are single-threaded too, gives the server every processor of the VM.
     */
    public record Server(int maxdop, int maxServerMemoryMegabytes, Integer processors) {

        /** The processors of the database in the measurement mode, as {@code CatalogServer} states them; CatalogRunTest.cs holds the values equal. */
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
        public static String measurementLeftOut(boolean configured, int maxdop, Integer processors) {
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
