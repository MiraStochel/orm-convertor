package cz.stochel.ormconvertor.javatests;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.SQLException;
import java.util.regex.Pattern;

/**
 * Where the Java suite takes its database from. The counterpart of the .NET
 * {@code TestDatabase}: the connection is configuration, not code, and no connection
 * string is in the repository (S4). One difference is deliberate - there is no skip.
 * The suite runs only where decision 076 puts a JVM, in the compose profile {@code test}
 * and on the CI runner, and both of those start a database of their own, so a missing
 * one is a failure here without any {@code ORMCONVERTOR_REQUIRE_TEST_DATABASE} to say so.
 */
public final class TestDatabase {

    /** JDBC URL of the test database, user and password included. */
    public static final String JDBC_URL_VARIABLE = "ORMCONVERTOR_TEST_JDBC_URL";

    /**
     * Schema the suite creates and drops. A different default from the .NET suite's
     * {@code ormconvertor_test}, so that both suites may run against one database at
     * the same time; the same override variable, because it means the same thing.
     */
    public static final String SCHEMA_VARIABLE = "ORMCONVERTOR_TEST_SCHEMA";
    public static final String DEFAULT_SCHEMA = "ormconvertor_java_test";

    // Substituted straight into DDL, hence restricted to a plain identifier.
    private static final Pattern IDENTIFIER = Pattern.compile("^[A-Za-z_][A-Za-z0-9_]*$");

    private TestDatabase() {
    }

    public static String jdbcUrl() {
        String url = System.getenv(JDBC_URL_VARIABLE);
        if (url == null || url.isBlank()) {
            throw new IllegalStateException(
                    "No test database configured. Set " + JDBC_URL_VARIABLE + " to a JDBC URL such as "
                    + "\"jdbc:sqlserver://localhost:1433;databaseName=ORMConvertorTests;user=sa;password=...;"
                    + "encrypt=true;trustServerCertificate=true\", or run the suite in its container with "
                    + "\"docker compose --profile test run --rm java_tests\".");
        }
        return url;
    }

    public static String schemaName() {
        String schema = System.getenv(SCHEMA_VARIABLE);
        if (schema == null || schema.isBlank()) {
            return DEFAULT_SCHEMA;
        }
        if (!IDENTIFIER.matcher(schema).matches()) {
            throw new IllegalStateException(
                    SCHEMA_VARIABLE + " must be a plain SQL identifier, not \"" + schema + "\".");
        }
        return schema;
    }

    /**
     * Opens a connection. Callers own their data: a test that writes rows wraps them in
     * a transaction and rolls it back, exactly as the .NET fixture's rule says.
     */
    public static Connection open() throws SQLException {
        return DriverManager.getConnection(jdbcUrl());
    }
}
