package cz.stochel.ormconvertor.javatests.fiben;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import java.util.ArrayList;
import java.util.List;

/**
 * The FIBEN query catalog as the running instance serves it at {@code /fiben} (decision 149).
 * Read as the contract puts it on the wire - camelCase names, enums as numbers - because this
 * suite shares no type with the server (decision 078): the catalog's texts and entities are
 * C# content, and the instance is where this suite takes them from, as it takes the artifacts.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record FibenCatalog(int sourceOrm, String dataSet, List<Unit> entities, List<Query> queries) {

    /** The translation states of the catalog, as {@code FibenTranslation} numbers them. */
    public static final int AS_SPECIFIED = 10;
    public static final int SIMPLIFIED = 20;
    public static final int NOT_TRANSLATED = 30;

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

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Unit(String name, int contentType, String content) {
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Query(
            String key,
            int number,
            int nesting,
            List<String> questions,
            List<String> tables,
            List<String> columns,
            boolean ordered,
            String sql,
            String benchmarkSql,
            int translation,
            String note) {
    }
}
