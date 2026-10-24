package cz.stochel.ormconvertor.javatests;

import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.regex.Pattern;

/**
 * Creates and drops the schema described by {@code TestSchema.sql} - the one script the
 * .NET fixture embeds, read here from {@code ../Tests/Database} as a test resource so
 * that the schema stays one (decision 076). Same placeholder, same batch separator, same
 * catalog-driven cleanup as {@code TestSchemaFixture}. Beside it, in the case-study database
 * ({@link CaseStudyDatabase}) and only where that database's instance is configured, the schemas
 * of the excerpted projects (decision 176), one per project from the {@code schema.sql} beside
 * each excerpt: the tables the case study maps, empty, so that the instance over that database
 * finds them in its catalog and the project's queries run over them at the fourth level.
 */
public final class TestSchema {

    private static final String SCRIPT_RESOURCE = "/TestSchema.sql";
    private static final String DATA_RESOURCE = "/Differential/FixtureData.sql";
    private static final String SHAPES_DATA_RESOURCE = "/QueryShapes/FixtureData.sql";
    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";

    /**
     * The excerpted projects whose schema the suite creates (decision 176), by the name of
     * their folder under {@code SampleData/ThirdParty}; the .NET suite holds this list equal to
     * the excerpts that carry a {@code schema.sql} ({@code Combined/CaseStudySchemaTest}).
     */
    public static final List<String> CASE_STUDIES = List.of("ambari", "chinook", "jpetstore", "keycloak");

    /**
     * Whether the key of an example is an excerpt of a real project - the case study of T1
     * (decision 174) -, whose tables stand in the schemas above: the key opens with the name of
     * the project's folder, {@code chinook-dapper} for {@code chinook}.
     */
    public static boolean isCaseStudy(String exampleKey) {
        int dash = exampleKey.indexOf('-');
        return dash > 0 && CASE_STUDIES.contains(exampleKey.substring(0, dash));
    }

    // GO is a client-side separator, not T-SQL; CREATE SCHEMA has to start its own batch.
    private static final Pattern BATCH_SEPARATOR = Pattern.compile("(?im)^\\s*GO\\s*$");

    /**
     * Written against the catalog rather than against a list of table names, so that a
     * table added to the script - or created by a framework under test - does not leave
     * a leftover behind.
     */
    private static final String DROP_SCHEMA_SQL = """
            DECLARE @schema NVARCHAR(128) = ?;
            DECLARE @sql NVARCHAR(MAX) = N'';

            SELECT @sql = @sql + N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
                               + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
            FROM sys.foreign_keys fk
            JOIN sys.tables t ON t.object_id = fk.parent_object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema;

            SELECT @sql = @sql + N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema;

            SELECT @sql = @sql + N'DROP SEQUENCE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(q.name) + N';'
            FROM sys.sequences q
            JOIN sys.schemas s ON s.schema_id = q.schema_id
            WHERE s.name = @schema;

            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schema)
                SET @sql = @sql + N'DROP SCHEMA ' + QUOTENAME(@schema) + N';';

            IF LEN(@sql) > 0
                EXEC sp_executesql @sql;
            """;

    private TestSchema() {
    }

    /** Drops whatever a crashed run may have left, then runs the scripts batch by batch - the fixture in the test database, the case studies in theirs. */
    public static void create() throws SQLException, IOException {
        try (Connection connection = TestDatabase.open()) {
            drop(connection, TestDatabase.schemaName());
            run(connection, batches());
        }

        if (CaseStudyDatabase.isConfigured()) {
            try (Connection connection = CaseStudyDatabase.open()) {
                dropCaseStudies(connection);
                run(connection, caseStudyBatches());
            }
        }
    }

    public static void drop() throws SQLException {
        try (Connection connection = TestDatabase.open()) {
            drop(connection, TestDatabase.schemaName());
        }

        if (CaseStudyDatabase.isConfigured()) {
            try (Connection connection = CaseStudyDatabase.open()) {
                dropCaseStudies(connection);
            }
        }
    }

    private static void run(Connection connection, List<String> batches) throws SQLException {
        for (String batch : batches) {
            try (Statement statement = connection.createStatement()) {
                statement.execute(batch);
            }
        }
    }

    private static void dropCaseStudies(Connection connection) throws SQLException {
        for (String project : CASE_STUDIES) {
            drop(connection, TestDatabase.caseStudySchemaName(project));
        }
    }

    private static void drop(Connection connection, String schema) throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement(DROP_SCHEMA_SQL)) {
            statement.setString(1, schema);
            statement.execute();
        }
    }

    /**
     * The batches of the DDL script followed by those of the read-only data the
     * differential verification reads (decision 089): the one table of the first six
     * queries of the matrix, then the seven tables of the domain the query categories of T2
     * are written over. The data belongs to the fixture and not to a test: it is written
     * once, never changed, and both suites make it from these scripts, so the two halves of
     * a pair read rows made by the same statements.
     */
    static List<String> batches() throws IOException {
        List<String> batches = new ArrayList<>(batchesOf(SCRIPT_RESOURCE, TestDatabase.schemaName()));
        batches.addAll(batchesOf(DATA_RESOURCE, TestDatabase.schemaName()));
        batches.addAll(batchesOf(SHAPES_DATA_RESOURCE, TestDatabase.schemaName()));
        return batches;
    }

    /**
     * The tables of every excerpted project, each in a schema of its own in the case-study
     * database (decision 176); the script leaves CREATE SCHEMA to its caller, as the LDBC scripts do.
     */
    static List<String> caseStudyBatches() throws IOException {
        List<String> batches = new ArrayList<>();
        for (String project : CASE_STUDIES) {
            String schema = TestDatabase.caseStudySchemaName(project);
            batches.add("CREATE SCHEMA [" + schema + "];");
            batches.addAll(batchesOf("/thirdparty/" + project + "/schema.sql", schema));
        }
        return batches;
    }

    private static List<String> batchesOf(String resource, String schema) throws IOException {
        try (InputStream stream = TestSchema.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException(
                        "Test resource " + resource + " is missing: the pom reads it from ../Tests/Database or ../SampleData/ThirdParty.");
            }
            String script = new String(stream.readAllBytes(), StandardCharsets.UTF_8)
                    .replace("﻿", "")
                    .replace(SCHEMA_PLACEHOLDER, schema);

            List<String> batches = new ArrayList<>();
            for (String batch : BATCH_SEPARATOR.split(script)) {
                String trimmed = batch.strip();
                if (!trimmed.isEmpty()) {
                    batches.add(trimmed);
                }
            }
            return batches;
        }
    }
}
