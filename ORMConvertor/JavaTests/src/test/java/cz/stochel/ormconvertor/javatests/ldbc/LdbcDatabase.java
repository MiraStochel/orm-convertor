package cz.stochel.ormconvertor.javatests.ldbc;

import static org.junit.jupiter.api.Assertions.fail;

import org.junit.jupiter.api.Assumptions;

/**
 * Where the judge of the LDBC catalog takes LdbcSnb and its instance of the tool from
 * (decision 117) - the counterpart of the .NET {@code LdbcDatabase}. The rest of this suite
 * has no skip, because it runs only where a database was started for it; the judge needs
 * more than that - the LDBC data set with the validation set beside it, and an instance whose
 * catalog is that database (decision 078) - so without its configuration it skips with the
 * reason, and fails with it where {@code ORMCONVERTOR_REQUIRE_LDBC_DATABASE} promised it.
 */
public final class LdbcDatabase {

    /** JDBC URL of LdbcSnb, user and password included. */
    public static final String JDBC_URL_VARIABLE = "ORMCONVERTOR_TEST_LDBC_JDBC_URL";

    /** Base address of an instance of the tool whose catalog is LdbcSnb, including the path {@code /orm}. */
    public static final String API_URL_VARIABLE = "ORMCONVERTOR_LDBC_API_URL";

    private static final String REQUIRE_VARIABLE = "ORMCONVERTOR_REQUIRE_LDBC_DATABASE";
    private static final String ROWS_VARIABLE = "ORMCONVERTOR_LDBC_VALIDATION_ROWS";

    /** The schema of the LDBC tables in LdbcSnb; the loader substitutes it for the placeholder of its scripts. */
    public static final String SCHEMA = "dbo";

    /**
     * The time limit of every command of a replay in seconds (decision 146): the reads of the
     * cells, which the frameworks run over connections opened from {@link #jdbcUrl()}, and the
     * judge's own inserts, compensation and counts alike. The URL carries it as the driver's
     * {@code queryTimeout}, the default of every statement created over the connection, so no
     * framework has to be told; a read over it fails its cell. The same value is the limit of
     * one execution in the Advisor's policy and in the .NET judge, which holds the three equal.
     */
    public static final int QUERY_TIMEOUT_SECONDS = 600;

    private static final String QUERY_TIMEOUT_PROPERTY = "queryTimeout";

    private LdbcDatabase() {
    }

    /** JDBC URL of LdbcSnb with the judge's time limit, or null when none is configured. */
    public static String jdbcUrl() {
        String configured = blankToNull(System.getenv(JDBC_URL_VARIABLE));
        return configured == null ? null : withQueryTimeout(configured);
    }

    /** The configured URL with {@link #QUERY_TIMEOUT_SECONDS} as its {@code queryTimeout}, whatever it carried. */
    static String withQueryTimeout(String jdbcUrl) {
        StringBuilder url = new StringBuilder();
        for (String part : jdbcUrl.split(";")) {
            int equals = part.indexOf('=');
            if (part.isBlank() || (equals > 0 && part.substring(0, equals).trim().equalsIgnoreCase(QUERY_TIMEOUT_PROPERTY))) {
                continue;
            }
            url.append(part).append(';');
        }
        return url.append(QUERY_TIMEOUT_PROPERTY).append('=').append(QUERY_TIMEOUT_SECONDS).toString();
    }

    public static String apiUrl() {
        String url = blankToNull(System.getenv(API_URL_VARIABLE));
        return url == null || !url.endsWith("/") ? url : url.substring(0, url.length() - 1);
    }

    /** Why the judge cannot run here for want of configuration, or null when it is configured. */
    public static String notConfigured() {
        if (jdbcUrl() != null && apiUrl() != null) {
            return null;
        }

        return "No LDBC database configured. Set " + JDBC_URL_VARIABLE + " to the JDBC URL of LdbcSnb with the data "
                + "set and the validation set loaded, and " + API_URL_VARIABLE + " to an instance of the tool whose "
                + "catalog is that database (the compose profile \"test\" starts it as test_app_ldbc) (decision 117).";
    }

    public static boolean required() {
        String value = System.getenv(REQUIRE_VARIABLE);
        return value != null && (value.strip().equals("1") || value.strip().equalsIgnoreCase("true"));
    }

    /** The number of lines a run replays, or null for the whole set. */
    public static Integer rows() {
        return resolveRows(System.getenv(ROWS_VARIABLE));
    }

    static Integer resolveRows(String configured) {
        if (configured == null || configured.isBlank()) {
            return null;
        }

        try {
            int rows = Integer.parseInt(configured.strip());
            if (rows > 0) {
                return rows;
            }
        } catch (NumberFormatException ignored) {
            // Reported below, as any other value that is not a positive number.
        }

        throw new IllegalStateException(ROWS_VARIABLE + " must be a positive number of lines, but was \"" + configured + "\".");
    }

    /** Skips the test with the reason, or fails it where the environment promised the judge. */
    public static void unavailable(String reason) {
        String failure = failureOf(reason, required());
        if (failure != null) {
            fail(failure);
        }

        Assumptions.abort(reason);
    }

    /** What a missing judge fails with, or null where it only skips with its reason. */
    static String failureOf(String reason, boolean required) {
        return required
                ? REQUIRE_VARIABLE + " is set, so this environment states that it provides LdbcSnb with the validation set - "
                        + "a skipped judge here would claim a verdict it does not have. " + reason
                : null;
    }

    private static String blankToNull(String value) {
        return value == null || value.isBlank() ? null : value.strip();
    }
}
