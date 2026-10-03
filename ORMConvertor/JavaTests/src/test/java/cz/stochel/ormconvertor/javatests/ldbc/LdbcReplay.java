package cz.stochel.ormconvertor.javatests.ldbc;

import cz.stochel.ormconvertor.javatests.differential.DifferentialMutation;
import cz.stochel.ormconvertor.javatests.differential.JavaQueryRunner;
import cz.stochel.ormconvertor.javatests.tool.ConversionResponse;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import cz.stochel.ormconvertor.javatests.tool.RecordKind;
import cz.stochel.ormconvertor.javatests.tool.ToolApi;
import cz.stochel.ormconvertor.javatests.tool.ToolResponse;
import java.lang.reflect.InvocationTargetException;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The replay of the validation set of LDBC Interactive v1 over LdbcSnb (decision 117) for the
 * three Java targets - the counterpart of {@code LdbcReplay.cs}. The lines are replayed in
 * their order: an insert runs its script from {@code database/ldbc/updates} and stays
 * committed, so that every artifact sees it from its own connection; a read runs the
 * generated artifact of Hibernate, EclipseLink and MyBatis and compares the rows with the
 * expected result. The compensation returns the database to its loaded state before the
 * replay and after it, and an application lock keeps a replay of the .NET suite from changing
 * the state meanwhile. The source run - the catalog's own text - is the .NET suite's, as the
 * Dapper runs of the differential matrix are.
 *
 * <p>The artifacts come from an instance whose catalog is LdbcSnb, the database they run on
 * (decision 078): the completion writes the schema of the tables into the mapping. The catalog
 * itself - the texts and their binding to the judge - comes from the same instance.
 */
final class LdbcReplay {

    /** The query whose artifact carries the mutations of decision 089. */
    static final String MUTATED_QUERY = "ic2";

    /** The frameworks whose artifacts this suite runs. */
    static final int[] FRAMEWORKS = {Orm.HIBERNATE, Orm.ECLIPSELINK, Orm.MYBATIS};

    /** The keys of the queries the set judges - every Interactive query of the catalog, which the .NET suite holds bound. */
    static final List<String> KEYS = List.of(
            "is1", "is2", "is3", "is4", "is5", "is6", "is7",
            "ic1", "ic2", "ic3", "ic4", "ic5", "ic6", "ic7", "ic8", "ic9", "ic10", "ic11", "ic12", "ic13", "ic14");

    private static final String LOCK_RESOURCE = "ldbc.validation";
    private static final int PAGE_SIZE = 500;

    private static LdbcReplay current;
    private static Exception failure;

    private final String unavailable;
    private final Map<String, Tally> tallies = new HashMap<>();
    private final Map<String, MutationTally> mutations = new HashMap<>();

    private LdbcCatalog catalog;
    private String setName;
    private int lines;
    private int inserts;
    private Duration elapsed = Duration.ZERO;
    private Map<String, Long> loaded = Map.of();
    private Map<String, Long> replayed = Map.of();
    private Map<String, Long> restored = Map.of();

    private LdbcReplay(String unavailable) {
        this.unavailable = unavailable;
    }

    /**
     * The replay of this run, made by the first test that asks for it. A replay that failed
     * fails every test after it with the same exception rather than starting again: one
     * replay takes minutes, and a second one would meet the same cause.
     */
    static synchronized LdbcReplay current() throws Exception {
        if (failure != null) {
            throw failure;
        }

        if (current == null) {
            try {
                current = replay();
            } catch (Exception e) {
                failure = e;
                throw e;
            }
        }
        return current;
    }

    void skipIfUnavailable() {
        if (unavailable != null) {
            LdbcDatabase.unavailable(unavailable);
        }
    }

    LdbcCatalog catalog() {
        return catalog;
    }

    String setName() {
        return setName;
    }

    int lines() {
        return lines;
    }

    int inserts() {
        return inserts;
    }

    Duration elapsed() {
        return elapsed;
    }

    Map<String, Long> loaded() {
        return loaded;
    }

    Map<String, Long> replayed() {
        return replayed;
    }

    Map<String, Long> restored() {
        return restored;
    }

    Tally tally(String key, int framework) {
        return tallies.get(key + "|" + framework);
    }

    MutationTally mutation(String mutation, int framework) {
        return mutations.get(mutation + "|" + framework);
    }

    private static LdbcReplay replay() throws Exception {
        String reason = LdbcDatabase.notConfigured();
        if (reason != null) {
            return new LdbcReplay(reason);
        }

        String jdbcUrl = LdbcDatabase.jdbcUrl();
        Connection control;
        String setName;

        try {
            control = DriverManager.getConnection(jdbcUrl);
            setName = scalar(control, "SELECT CAST(value AS NVARCHAR(200)) FROM sys.extended_properties WHERE class = 0 AND name = N'ldbc.validation'");
        } catch (SQLException e) {
            return new LdbcReplay("The LDBC database " + LdbcDatabase.JDBC_URL_VARIABLE + " names cannot be reached: " + e.getMessage());
        }

        try (control) {
            if (setName == null) {
                return new LdbcReplay("The LDBC database holds no validation set: the extended property ldbc.validation is missing. "
                        + "database/ldbc/load-ldbc.sh loads it on the start of the container; elsewhere run database/ldbc/validation.sql (decision 117).");
            }

            ToolApi.awaitReady(LdbcDatabase.apiUrl());

            LdbcReplay replay = new LdbcReplay(null);
            replay.setName = setName;
            replay.catalog = ToolApi.get(LdbcDatabase.apiUrl(), "/ldbc", LdbcCatalog.class);
            replay.run(control, jdbcUrl);
            return replay;
        }
    }

    private void run(Connection control, String jdbcUrl) throws Exception {
        long start = System.nanoTime();

        Map<String, Map<Integer, PreparedJavaQuery>> prepared = prepare(jdbcUrl);
        Map<String, PreparedJavaQuery> mutated = prepareMutations(jdbcUrl);

        try {
            // Held for the lifetime of this connection: a replay of the other suite waits, and
            // a replay that dies releases the lock with its connection.
            try (Statement statement = control.createStatement()) {
                statement.execute("DECLARE @granted INT; EXEC @granted = sp_getapplock @Resource = N'" + LOCK_RESOURCE
                        + "', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = -1; "
                        + "IF @granted < 0 THROW 50117, 'The application lock " + LOCK_RESOURCE + " was not granted.', 1;");
            }

            LdbcUpdates.undo(control);
            loaded = counts(control);

            try {
                replay(control, prepared, mutated);
                replayed = counts(control);
            } finally {
                LdbcUpdates.undo(control);
            }

            restored = counts(control);
        } finally {
            for (Map<Integer, PreparedJavaQuery> cells : prepared.values()) {
                for (PreparedJavaQuery query : cells.values()) {
                    if (query != null) {
                        query.close();
                    }
                }
            }
            for (PreparedJavaQuery query : mutated.values()) {
                if (query != null) {
                    query.close();
                }
            }
            PreparedJavaQuery.closeAll();
        }

        elapsed = Duration.ofNanos(System.nanoTime() - start);
    }

    /** Every judged query translated into every Java target and compiled; a cell that cannot be made says why. */
    private Map<String, Map<Integer, PreparedJavaQuery>> prepare(String jdbcUrl) {
        Map<String, Map<Integer, PreparedJavaQuery>> prepared = new HashMap<>();

        for (String key : KEYS) {
            LdbcCatalog.Query query = catalog.query(key);
            Map<Integer, PreparedJavaQuery> cells = prepared.computeIfAbsent(query.validation().operation(), operation -> new LinkedHashMap<>());

            for (int framework : FRAMEWORKS) {
                Tally tally = new Tally();
                tallies.put(key + "|" + framework, tally);

                try {
                    cells.put(framework, PreparedJavaQuery.prepare(key, framework, translate(query, framework), null, jdbcUrl));
                } catch (Throwable e) {
                    tally.unprepared = innermost(e).toString();
                    cells.put(framework, null);
                }
            }
        }

        return prepared;
    }

    /** The mutations of decision 089, applied to the artifact of one query in every Java target. */
    private Map<String, PreparedJavaQuery> prepareMutations(String jdbcUrl) {
        LdbcCatalog.Query query = catalog.query(MUTATED_QUERY);
        Map<String, PreparedJavaQuery> prepared = new LinkedHashMap<>();

        for (int framework : FRAMEWORKS) {
            ConversionResponse response = translate(query, framework);
            String artifact = JavaQueryRunner.mutableArtifact(response, framework, MUTATED_QUERY);

            for (DifferentialMutation mutation : DifferentialMutation.all()) {
                String text = mutation.apply().apply(artifact);
                MutationTally tally = new MutationTally(!text.equals(artifact));
                String cell = mutation.key() + "|" + framework;
                mutations.put(cell, tally);
                prepared.put(cell, null);

                if (!tally.changed) {
                    continue;
                }

                try {
                    prepared.put(cell, PreparedJavaQuery.prepare(MUTATED_QUERY, framework, response, text, jdbcUrl));
                } catch (Throwable e) {
                    tally.detection = "the mutated artifact cannot be made: " + innermost(e);
                }
            }
        }

        return prepared;
    }

    private void replay(
            Connection control,
            Map<String, Map<Integer, PreparedJavaQuery>> prepared,
            Map<String, PreparedJavaQuery> mutated) throws SQLException {

        Map<String, LdbcCatalog.Query> queries = new HashMap<>();
        for (String key : KEYS) {
            LdbcCatalog.Query query = catalog.query(key);
            queries.put(query.validation().operation(), query);
        }

        int last = LdbcDatabase.rows() != null ? LdbcDatabase.rows() : Integer.MAX_VALUE;
        int after = 0;

        while (true) {
            List<Line> page = page(control, after, last);
            if (page.isEmpty()) {
                return;
            }

            for (Line line : page) {
                after = line.position();
                lines++;

                if (line.operation().startsWith("INS")) {
                    LdbcUpdates.insert(control, line.operation(), line.parameters());
                    inserts++;
                    continue;
                }

                LdbcCatalog.Query query = queries.get(line.operation());
                List<String> expected = LdbcCanonicalForm.expected(query.validation(), line.result());

                for (Map.Entry<Integer, PreparedJavaQuery> cell : prepared.get(line.operation()).entrySet()) {
                    if (cell.getValue() != null) {
                        judge(tallies.get(query.key() + "|" + cell.getKey()), query, cell.getValue(), line, expected);
                    }
                }

                if (query.key().equals(MUTATED_QUERY)) {
                    catchMutations(mutated, query, line, expected);
                }
            }
        }
    }

    private static void judge(Tally tally, LdbcCatalog.Query query, PreparedJavaQuery artifact, Line line, List<String> expected) {
        try {
            List<String> actual = run(query, artifact, line.parameters());
            if (actual.equals(expected)) {
                tally.match();
            } else {
                tally.mismatch(line.position(), expected, actual);
            }
        } catch (Throwable e) {
            tally.fail(line.position(), e);
        }
    }

    /** Runs every mutation not caught yet on this read; the first read that tells it apart catches it. */
    private void catchMutations(Map<String, PreparedJavaQuery> mutated, LdbcCatalog.Query query, Line line, List<String> expected) {
        for (Map.Entry<String, PreparedJavaQuery> cell : mutated.entrySet()) {
            MutationTally tally = mutations.get(cell.getKey());
            if (cell.getValue() == null || tally.detection != null) {
                continue;
            }

            tally.reads++;
            try {
                if (!run(query, cell.getValue(), line.parameters()).equals(expected)) {
                    tally.detection = "line " + line.position() + " returned other rows than the judge expects";
                }
            } catch (Throwable e) {
                tally.detection = "line " + line.position() + " failed to run: " + innermost(e);
            }
        }
    }

    private static List<String> run(LdbcCatalog.Query query, PreparedJavaQuery artifact, String parameters) throws Exception {
        LdbcCatalog.Validation binding = query.validation();
        Object[] arguments = LdbcCanonicalForm.arguments(query, artifact.parameterNames(), parameters);

        List<String> columns = new ArrayList<>();
        for (LdbcCatalog.Field field : binding.fields()) {
            columns.add(field.column());
        }

        return LdbcCanonicalForm.actual(binding, artifact.run(query.key(), arguments, columns));
    }

    /** The artifact of one target, from Dapper with LdbcSnb as the catalog; a Failure record makes the cell unpreparable. */
    private ConversionResponse translate(LdbcCatalog.Query query, int framework) {
        ToolResponse answer = ToolApi.convert(LdbcDatabase.apiUrl(), Orm.DAPPER, framework, catalog.units(query));
        ConversionResponse response = answer.required();

        if (!response.recordsOf(RecordKind.FAILURE).isEmpty()) {
            throw new IllegalStateException(query.key() + " into " + Orm.nameOf(framework) + " was refused:"
                    + System.lineSeparator() + response.describeRecords());
        }

        return response;
    }

    private record Line(int position, String operation, String parameters, String result) {
    }

    private static List<Line> page(Connection control, int after, int last) throws SQLException {
        List<Line> page = new ArrayList<>(PAGE_SIZE);
        try (PreparedStatement statement = control.prepareStatement(
                "SELECT TOP (" + PAGE_SIZE + ") [Position], [Operation], [Parameters], [Result] FROM [" + LdbcDatabase.SCHEMA
                        + "].[ValidationOperation] WHERE [Position] > ? AND [Position] <= ? ORDER BY [Position];")) {
            statement.setInt(1, after);
            statement.setInt(2, last);
            try (ResultSet rows = statement.executeQuery()) {
                while (rows.next()) {
                    page.add(new Line(rows.getInt(1), rows.getString(2), rows.getNString(3), rows.getNString(4)));
                }
            }
        }
        return page;
    }

    /** The rows of every LDBC table, by the names of the catalog's entities - which are the tables of schema.sql. */
    private Map<String, Long> counts(Connection control) throws SQLException {
        Map<String, Long> counts = new LinkedHashMap<>();
        for (LdbcCatalog.Unit entity : catalog.entities()) {
            String table = entity.name().substring(0, entity.name().lastIndexOf('.'));
            counts.put(table, Long.valueOf(scalar(control, "SELECT CAST(COUNT_BIG(*) AS NVARCHAR(30)) FROM [" + LdbcDatabase.SCHEMA + "].[" + table + "];")));
        }
        return counts;
    }

    private static String scalar(Connection connection, String sql) throws SQLException {
        try (Statement statement = connection.createStatement(); ResultSet rows = statement.executeQuery(sql)) {
            return rows.next() ? rows.getString(1) : null;
        }
    }

    /** The exception a generated method threw, not the reflection that called it. */
    static Throwable innermost(Throwable e) {
        Throwable current = e;
        while (current instanceof InvocationTargetException && current.getCause() != null) {
            current = current.getCause();
        }
        return current;
    }

    /** What one artifact did over the reads of its operation. */
    static final class Tally {

        private static final int LISTED = 3;

        int reads;
        int matches;
        int failures;
        String unprepared;
        final List<String> disagreements = new ArrayList<>();

        void match() {
            reads++;
            matches++;
        }

        void mismatch(int position, List<String> expected, List<String> actual) {
            reads++;
            if (disagreements.size() < LISTED) {
                int index = 0;
                while (index < expected.size() && index < actual.size() && expected.get(index).equals(actual.get(index))) {
                    index++;
                }
                disagreements.add("line " + position + ": the judge expects " + expected.size() + " rows, the artifact returned "
                        + actual.size() + System.lineSeparator() + "    row " + (index + 1) + ":"
                        + System.lineSeparator() + "      expected " + (index < expected.size() ? expected.get(index) : "(no row)")
                        + System.lineSeparator() + "      actual   " + (index < actual.size() ? actual.get(index) : "(no row)"));
            }
        }

        void fail(int position, Throwable e) {
            reads++;
            failures++;
            if (disagreements.size() < LISTED) {
                disagreements.add("line " + position + ": " + innermost(e));
            }
        }

        String describe(String cell) {
            if (unprepared != null) {
                return cell + ": the artifact could not be made: " + unprepared;
            }

            StringBuilder text = new StringBuilder(cell + ": " + matches + " of " + reads + " reads agree with the judge, "
                    + failures + " failed to run.");
            for (String disagreement : disagreements) {
                text.append(System.lineSeparator()).append("  ").append(disagreement);
            }
            return text.toString();
        }
    }

    /** Whether a deliberately wrong artifact was caught by the judge. */
    static final class MutationTally {

        final boolean changed;
        String detection;
        int reads;

        MutationTally(boolean changed) {
            this.changed = changed;
        }
    }
}
