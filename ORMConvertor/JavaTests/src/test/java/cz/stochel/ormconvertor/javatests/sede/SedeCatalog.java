package cz.stochel.ormconvertor.javatests.sede;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.databind.ObjectMapper;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.util.ArrayList;
import java.util.List;

/**
 * The SEDE query catalog as the running instance serves it at {@code /sede} (decision 208).
 * Read as the contract puts it on the wire - camelCase names, enums as numbers - because this
 * suite shares no type with the server (decision 078): the catalog's texts and entities are
 * C# content, and the instance is where this suite takes them from, as it takes the artifacts.
 * The keys alone are also read from the file the repository carries, {@code SampleData/Sede/
 * queries.json}, which the pom maps in, so that the cases can be stated where no instance is
 * configured.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record SedeCatalog(int sourceOrm, String dataSet, List<Unit> entities, List<Query> queries) {

    /** The translation states of the catalog, as {@code SedeTranslation} numbers them. */
    public static final int AS_SPECIFIED = 10;
    public static final int SIMPLIFIED = 20;
    public static final int NOT_TRANSLATED = 30;

    /** The parts of the set, as {@code SedeSet} numbers them. */
    public static final int VALIDATION = 10;
    public static final int TEST = 20;

    private static final String RESOURCE = "/Sede/queries.json";
    private static final ObjectMapper MAPPER = new ObjectMapper();

    public Query query(String key) {
        return queries.stream()
                .filter(query -> query.key().equals(key))
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("The catalog has no query " + key + "."));
    }

    /**
     * The units of a conversion of one query: the entity classes of the tables it reads - and
     * only those, as the decision has it - then the query's text as one more unit.
     */
    public List<InputUnit> units(Query query) {
        List<InputUnit> units = new ArrayList<>();
        for (Unit entity : entities) {
            String table = entity.name().substring(0, entity.name().lastIndexOf('.'));
            if (query.tables().stream().anyMatch(table::equalsIgnoreCase)) {
                units.add(new InputUnit(entity.name(), entity.contentType(), entity.content()));
            }
        }

        units.add(new InputUnit(query.key() + ".sql", ContentType.SQL_QUERY, query.sql()));
        return units;
    }

    /** The keys of the catalog as the repository carries it, in its order, read without an instance. */
    public static List<String> keysOfTheRepository() {
        try (InputStream stream = SedeCatalog.class.getResourceAsStream(RESOURCE)) {
            if (stream == null) {
                throw new IllegalStateException("Test resource " + RESOURCE + " is missing: the pom reads it from ../SampleData/Sede.");
            }
            return MAPPER.readValue(stream, Document.class).queries().stream().map(Key::key).toList();
        } catch (IOException e) {
            throw new UncheckedIOException("Test resource " + RESOURCE + " could not be read.", e);
        }
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    private record Document(List<Key> queries) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    private record Key(String key) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Unit(String name, int contentType, String content) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Parameter(String name, String sqlType, String value) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Query(
            String key,
            int querySetId,
            int set,
            String title,
            String description,
            List<Parameter> parameters,
            List<String> tables,
            List<String> columns,
            boolean ordered,
            List<String> orderingKey,
            String sql,
            String sedeSql,
            int translation,
            String note) {
    }
}
