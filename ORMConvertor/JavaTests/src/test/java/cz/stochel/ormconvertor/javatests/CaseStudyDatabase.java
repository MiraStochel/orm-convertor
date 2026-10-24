package cz.stochel.ormconvertor.javatests;

import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.SQLException;

/**
 * Where the case studies take their database and their instance of the tool from (decision
 * 176) - the counterpart of the .NET {@code TestDatabase.CaseStudyConnectionString}. The
 * database beside the test database holds the tables of the excerpted projects, empty, one
 * schema per project; {@link TestSchema} creates them there. The experiment run translates a
 * case study by an instance whose catalog is that database (decision 078), so the suite needs
 * its address; without it the case studies are left out of the run and the schemas are not
 * created, exactly as the LDBC judge is left out without its instance.
 */
public final class CaseStudyDatabase {

    /** JDBC URL of the case-study database; optional - without it the test database's URL with {@link #DATABASE_NAME} as its database. */
    public static final String JDBC_URL_VARIABLE = "ORMCONVERTOR_TEST_CASESTUDY_JDBC_URL";

    /** Base address of an instance of the tool whose catalog is the case-study database, including the path {@code /orm}. */
    public static final String API_URL_VARIABLE = "ORMCONVERTOR_CASESTUDY_API_URL";

    /** The database on the server of the test database, where the environment creates it as it creates the test database. */
    public static final String DATABASE_NAME = "ORMConvertorCaseStudies";

    private static final String DATABASE_PROPERTY = "databaseName";

    private CaseStudyDatabase() {
    }

    /** Whether the case studies can be translated and run here: the instance over the case-study database is configured. */
    public static boolean isConfigured() {
        return apiUrl() != null;
    }

    public static String apiUrl() {
        String url = System.getenv(API_URL_VARIABLE);
        if (url == null || url.isBlank()) {
            return null;
        }
        return url.endsWith("/") ? url.substring(0, url.length() - 1) : url;
    }

    /** JDBC URL of the case-study database: the configured one, otherwise the test database's with the case-study database in it. */
    public static String jdbcUrl() {
        String configured = System.getenv(JDBC_URL_VARIABLE);
        if (configured != null && !configured.isBlank()) {
            return configured.strip();
        }

        StringBuilder url = new StringBuilder();
        boolean replaced = false;
        for (String part : TestDatabase.jdbcUrl().split(";")) {
            if (part.isBlank()) {
                continue;
            }
            int equals = part.indexOf('=');
            if (equals > 0 && part.substring(0, equals).trim().equalsIgnoreCase(DATABASE_PROPERTY)) {
                url.append(DATABASE_PROPERTY).append('=').append(DATABASE_NAME).append(';');
                replaced = true;
            } else {
                url.append(part).append(';');
            }
        }
        if (!replaced) {
            url.append(DATABASE_PROPERTY).append('=').append(DATABASE_NAME).append(';');
        }
        return url.toString();
    }

    /** Why a case study cannot be translated here, or null where it can. */
    public static String notConfigured() {
        if (isConfigured()) {
            return null;
        }
        return "No case-study instance configured. Set " + API_URL_VARIABLE + " to an instance of the tool whose catalog is "
                + "the database " + DATABASE_NAME + " (the compose profile \"test\" starts it as test_app_cases), and "
                + JDBC_URL_VARIABLE + " where that database is not on the server of the test database (decision 176).";
    }

    public static Connection open() throws SQLException {
        return DriverManager.getConnection(jdbcUrl());
    }
}
