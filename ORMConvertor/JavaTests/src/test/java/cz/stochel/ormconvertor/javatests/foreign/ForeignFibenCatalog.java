package cz.stochel.ormconvertor.javatests.foreign;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.sql.Connection;
import java.sql.DriverManager;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The FIBEN catalog as the repository carries it, {@code SampleData/Fiben/queries.json}, read
 * as a test resource the pom maps in - what the path of a foreign artifact needs of a query
 * (decision 172): the columns of its result and the key of its ordering, so that the rows
 * can be put into the canonical form (decisions 089 and 170). The on-demand run takes the
 * catalog from an instance of the tool (decision 149); the path takes it from the file,
 * because a foreign artifact is judged without an instance.
 */
public final class ForeignFibenCatalog {

    private static final String RESOURCE = "/Fiben/queries.json";
    private static final ObjectMapper MAPPER = new ObjectMapper();

    private static List<Query> queries;

    private ForeignFibenCatalog() {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    private record Document(List<Query> queries) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Query(String key, List<String> columns, List<String> orderingKey) {
    }

    /** One entry of the judge: the digest of the whole result and the rows the judge keeps, if any. */
    public record Entry(String digest, List<String> rows) {
    }

    public static synchronized Query query(String key) {
        if (queries == null) {
            try (InputStream stream = ForeignFibenCatalog.class.getResourceAsStream(RESOURCE)) {
                if (stream == null) {
                    throw new IllegalStateException("Test resource " + RESOURCE + " is missing: the pom reads it from ../SampleData/Fiben.");
                }
                queries = MAPPER.readValue(stream, Document.class).queries();
            } catch (IOException e) {
                throw new UncheckedIOException("Test resource " + RESOURCE + " could not be read.", e);
            }
        }

        return queries.stream()
                .filter(query -> query.key().equalsIgnoreCase(key))
                .findFirst()
                .orElse(null);
    }

    /** The judge of the catalog in the Fiben database (decision 149), by query key; null where the database holds none. */
    public static Map<String, Entry> judge(String jdbcUrl) throws SQLException {
        try (Connection connection = DriverManager.getConnection(jdbcUrl)) {
            try (Statement statement = connection.createStatement();
                 ResultSet exists = statement.executeQuery("SELECT CAST(OBJECT_ID(N'[dbo].[Judge]', N'U') AS NVARCHAR(30))")) {
                if (!exists.next() || exists.getString(1) == null) {
                    return null;
                }
            }

            Map<String, String> digests = new HashMap<>();
            Map<String, List<String>> rows = new HashMap<>();
            try (Statement statement = connection.createStatement();
                 ResultSet result = statement.executeQuery("SELECT [QueryKey], [Position], [Rendered] FROM [dbo].[Judge] ORDER BY [QueryKey], [Position];")) {
                while (result.next()) {
                    String key = result.getString(1).toLowerCase(java.util.Locale.ROOT);
                    if (result.getInt(2) == 0) {
                        digests.put(key, result.getNString(3));
                    } else {
                        rows.computeIfAbsent(key, k -> new ArrayList<>()).add(result.getNString(3));
                    }
                }
            }

            Map<String, Entry> judge = new HashMap<>();
            digests.forEach((key, digest) -> judge.put(key, new Entry(digest, rows.getOrDefault(key, List.of()))));
            return judge;
        }
    }
}
