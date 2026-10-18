package cz.stochel.ormconvertor.javatests.fiben;

import static org.junit.jupiter.api.Assertions.fail;

import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.Set;
import java.util.stream.Collectors;
import org.junit.jupiter.api.Assumptions;

/**
 * Where the on-demand run of the FIBEN catalog takes the Fiben database and its instance of
 * the tool from (decision 149) - the counterpart of the .NET {@code FibenDatabase}. The run
 * needs the benchmark loaded and an instance whose catalog is that database (decision 078),
 * so without its configuration it skips with the reason, and fails with it where
 * {@code ORMCONVERTOR_REQUIRE_FIBEN_DATABASE} promised it.
 */
public final class FibenDatabase {

    /** JDBC URL of Fiben, user and password included. */
    public static final String JDBC_URL_VARIABLE = "ORMCONVERTOR_TEST_FIBEN_JDBC_URL";

    /** Base address of an instance of the tool whose catalog is Fiben, including the path {@code /orm}. */
    public static final String API_URL_VARIABLE = "ORMCONVERTOR_FIBEN_API_URL";

    private static final String REQUIRE_VARIABLE = "ORMCONVERTOR_REQUIRE_FIBEN_DATABASE";
    private static final String QUERIES_VARIABLE = "ORMCONVERTOR_FIBEN_QUERIES";
    private static final String RESULTS_VARIABLE = "ORMCONVERTOR_FIBEN_RESULTS";
    private static final String MEASURE_VARIABLE = "ORMCONVERTOR_FIBEN_MEASURE";

    /** The schema of the FIBEN tables and of the judge in the Fiben database. */
    public static final String SCHEMA = "dbo";

    private static final String QUERY_TIMEOUT_PROPERTY = "queryTimeout";

    private FibenDatabase() {
    }

    /** JDBC URL of Fiben with the run's time limit (decision 146), or null when none is configured. */
    public static String jdbcUrl() {
        String configured = blankToNull(System.getenv(JDBC_URL_VARIABLE));
        return configured == null ? null : withQueryTimeout(configured);
    }

    /** The configured URL with {@link FibenRun#QUERY_TIMEOUT_SECONDS} as its {@code queryTimeout}, whatever it carried. */
    static String withQueryTimeout(String jdbcUrl) {
        StringBuilder url = new StringBuilder();
        for (String part : jdbcUrl.split(";")) {
            int equals = part.indexOf('=');
            if (part.isBlank() || (equals > 0 && part.substring(0, equals).trim().equalsIgnoreCase(QUERY_TIMEOUT_PROPERTY))) {
                continue;
            }
            url.append(part).append(';');
        }
        return url.append(QUERY_TIMEOUT_PROPERTY).append('=').append(FibenRun.QUERY_TIMEOUT_SECONDS).toString();
    }

    public static String apiUrl() {
        String url = blankToNull(System.getenv(API_URL_VARIABLE));
        return url == null || !url.endsWith("/") ? url : url.substring(0, url.length() - 1);
    }

    /** Why the run cannot happen here for want of configuration, or null when it is configured. */
    public static String notConfigured() {
        if (jdbcUrl() != null && apiUrl() != null) {
            return null;
        }

        return "No FIBEN database configured. Set " + JDBC_URL_VARIABLE + " to the JDBC URL of Fiben with the benchmark "
                + "loaded, and " + API_URL_VARIABLE + " to an instance of the tool whose catalog is that database "
                + "(ORMCONVERTOR_CATALOG_DATABASE=Fiben docker compose up ormconvertor) (decision 149).";
    }

    public static boolean required() {
        return flag(System.getenv(REQUIRE_VARIABLE));
    }

    /** The keys a run is limited to, or null for the whole catalog. */
    public static Set<String> keys() {
        return resolveKeys(System.getenv(QUERIES_VARIABLE));
    }

    static Set<String> resolveKeys(String configured) {
        if (configured == null || configured.isBlank()) {
            return null;
        }

        Set<String> keys = Arrays.stream(configured.split(","))
                .map(String::strip)
                .filter(key -> !key.isEmpty())
                .map(key -> key.toLowerCase(java.util.Locale.ROOT))
                .collect(Collectors.toCollection(LinkedHashSet::new));
        return keys.isEmpty() ? null : keys;
    }

    /** The directory the run writes its files into, or null for the summary alone. */
    public static String resultsDirectory() {
        return blankToNull(System.getenv(RESULTS_VARIABLE));
    }

    /** Whether the run measures (decision 145) after it has compared; 0 leaves the measurement out. */
    public static boolean measures() {
        String value = System.getenv(MEASURE_VARIABLE);
        return value == null || value.isBlank() || flag(value);
    }

    /** Skips the test with the reason, or fails it where the environment promised the database. */
    public static void unavailable(String reason) {
        String failure = failureOf(reason, required());
        if (failure != null) {
            fail(failure);
        }

        Assumptions.abort(reason);
    }

    static String failureOf(String reason, boolean required) {
        return required
                ? REQUIRE_VARIABLE + " is set, so this environment states that it provides the Fiben database - "
                        + "a skipped run here would claim a result it does not have. " + reason
                : null;
    }

    private static boolean flag(String value) {
        return value != null && (value.strip().equals("1") || value.strip().equalsIgnoreCase("true"));
    }

    private static String blankToNull(String value) {
        return value == null || value.isBlank() ? null : value.strip();
    }
}
