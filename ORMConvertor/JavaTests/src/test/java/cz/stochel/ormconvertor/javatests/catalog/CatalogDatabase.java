package cz.stochel.ormconvertor.javatests.catalog;

import static org.junit.jupiter.api.Assertions.fail;

import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.Locale;
import java.util.Set;
import java.util.stream.Collectors;
import org.junit.jupiter.api.Assumptions;

/**
 * Where the on-demand run of a query catalog takes its database and its instance of the tool
 * from - the counterpart of the .NET {@code CatalogDatabase}: {@code Fiben} for the FIBEN catalog
 * (decision 149), {@code Sede} for the SEDE catalog (decision 208). The run needs the data loaded
 * and an instance whose catalog is that database (decision 078), so without its configuration it
 * skips with the reason, and fails with it where the environment promised it. One class for both
 * catalogs: the names of the settings differ by the prefix alone.
 */
public final class CatalogDatabase {

    public static final CatalogDatabase FIBEN = new CatalogDatabase("FIBEN", "Fiben", "FIBEN", "149");
    public static final CatalogDatabase SEDE = new CatalogDatabase("SEDE", "Sede", "SEDE", "208");

    /** The schema of the catalog's tables and of the judge in its database. */
    public static final String SCHEMA = "dbo";

    private static final String QUERY_TIMEOUT_PROPERTY = "queryTimeout";

    private final String name;
    private final String databaseName;
    private final String prefix;
    private final String decision;

    private CatalogDatabase(String name, String databaseName, String prefix, String decision) {
        this.name = name;
        this.databaseName = databaseName;
        this.prefix = prefix;
        this.decision = decision;
    }

    /** The catalog's name as the records write it: FIBEN, SEDE. */
    public String name() {
        return name;
    }

    /** The database of the container: Fiben, Sede. */
    public String databaseName() {
        return databaseName;
    }

    /** The decision the catalog rests on. */
    public String decision() {
        return decision;
    }

    /** JDBC URL of the database, user and password included. */
    public String jdbcUrlVariable() {
        return "ORMCONVERTOR_TEST_" + prefix + "_JDBC_URL";
    }

    /** Base address of an instance of the tool whose catalog is the database, including the path {@code /orm}. */
    public String apiUrlVariable() {
        return "ORMCONVERTOR_" + prefix + "_API_URL";
    }

    public String requireVariable() {
        return "ORMCONVERTOR_REQUIRE_" + prefix + "_DATABASE";
    }

    public String queriesVariable() {
        return "ORMCONVERTOR_" + prefix + "_QUERIES";
    }

    public String resultsVariable() {
        return "ORMCONVERTOR_" + prefix + "_RESULTS";
    }

    public String measureVariable() {
        return "ORMCONVERTOR_" + prefix + "_MEASURE";
    }

    /** JDBC URL with the run's time limit (decision 146), or null when none is configured. */
    public String jdbcUrl() {
        String configured = blankToNull(System.getenv(jdbcUrlVariable()));
        return configured == null ? null : withQueryTimeout(configured);
    }

    /** The configured URL with {@link CatalogRun#QUERY_TIMEOUT_SECONDS} as its {@code queryTimeout}, whatever it carried. */
    static String withQueryTimeout(String jdbcUrl) {
        StringBuilder url = new StringBuilder();
        for (String part : jdbcUrl.split(";")) {
            int equals = part.indexOf('=');
            if (part.isBlank() || (equals > 0 && part.substring(0, equals).trim().equalsIgnoreCase(QUERY_TIMEOUT_PROPERTY))) {
                continue;
            }
            url.append(part).append(';');
        }
        return url.append(QUERY_TIMEOUT_PROPERTY).append('=').append(CatalogRun.QUERY_TIMEOUT_SECONDS).toString();
    }

    public String apiUrl() {
        String url = blankToNull(System.getenv(apiUrlVariable()));
        return url == null || !url.endsWith("/") ? url : url.substring(0, url.length() - 1);
    }

    /** Why the run cannot happen here for want of configuration, or null when it is configured. */
    public String notConfigured() {
        if (jdbcUrl() != null && apiUrl() != null) {
            return null;
        }

        return "No " + name + " database configured. Set " + jdbcUrlVariable() + " to the JDBC URL of " + databaseName + " with the data "
                + "loaded, and " + apiUrlVariable() + " to an instance of the tool whose catalog is that database "
                + "(ORMCONVERTOR_CATALOG_DATABASE=" + databaseName + " docker compose up ormconvertor) (decision " + decision + ").";
    }

    public boolean required() {
        return flag(System.getenv(requireVariable()));
    }

    /** The keys a run is limited to, or null for the whole catalog. */
    public Set<String> keys() {
        return resolveKeys(System.getenv(queriesVariable()));
    }

    static Set<String> resolveKeys(String configured) {
        if (configured == null || configured.isBlank()) {
            return null;
        }

        Set<String> keys = Arrays.stream(configured.split(","))
                .map(String::strip)
                .filter(key -> !key.isEmpty())
                .map(key -> key.toLowerCase(Locale.ROOT))
                .collect(Collectors.toCollection(LinkedHashSet::new));
        return keys.isEmpty() ? null : keys;
    }

    /** The directory the run writes its files into, or null for the summary alone. */
    public String resultsDirectory() {
        return blankToNull(System.getenv(resultsVariable()));
    }

    /** Whether the run measures (decision 145) after it has compared; 0 leaves the measurement out. */
    public boolean measures() {
        String value = System.getenv(measureVariable());
        return value == null || value.isBlank() || flag(value);
    }

    /** Skips the test with the reason, or fails it where the environment promised the database. */
    public void unavailable(String reason) {
        String failure = failureOf(reason, required());
        if (failure != null) {
            fail(failure);
        }

        Assumptions.abort(reason);
    }

    String failureOf(String reason, boolean required) {
        return required
                ? requireVariable() + " is set, so this environment states that it provides the " + databaseName + " database - "
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
