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
 * catalog-driven cleanup as {@code TestSchemaFixture}.
 */
public final class TestSchema {

    private static final String SCRIPT_RESOURCE = "/TestSchema.sql";
    private static final String DATA_RESOURCE = "/Differential/FixtureData.sql";
    private static final String SCHEMA_PLACEHOLDER = "{{schema}}";

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

    /** Drops whatever a crashed run may have left, then runs the script batch by batch. */
    public static void create() throws SQLException, IOException {
        try (Connection connection = TestDatabase.open()) {
            drop(connection);
            for (String batch : batches()) {
                try (Statement statement = connection.createStatement()) {
                    statement.execute(batch);
                }
            }
        }
    }

    public static void drop() throws SQLException {
        try (Connection connection = TestDatabase.open()) {
            drop(connection);
        }
    }

    private static void drop(Connection connection) throws SQLException {
        try (PreparedStatement statement = connection.prepareStatement(DROP_SCHEMA_SQL)) {
            statement.setString(1, TestDatabase.schemaName());
            statement.execute();
        }
    }

    /**
     * The batches of the DDL script followed by those of the read-only data the
     * differential verification reads (decision 089). The data belongs to the fixture and
     * not to a test: it is written once, never changed, and both suites make it from this
     * one script, so the two halves of a pair read rows made by the same statements.
     */
    static List<String> batches() throws IOException {
        List<String> batches = new ArrayList<>(batchesOf(SCRIPT_RESOURCE));
        batches.addAll(batchesOf(DATA_RESOURCE));
        return batches;
    }

    private static List<String> batchesOf(String resource) throws IOException {
        try (InputStream stream = TestSchema.class.getResourceAsStream(resource)) {
            if (stream == null) {
                throw new IllegalStateException(
                        "Test resource " + resource + " is missing: the pom reads it from ../Tests/Database.");
            }
            String script = new String(stream.readAllBytes(), StandardCharsets.UTF_8)
                    .replace("﻿", "")
                    .replace(SCHEMA_PLACEHOLDER, TestDatabase.schemaName());

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
