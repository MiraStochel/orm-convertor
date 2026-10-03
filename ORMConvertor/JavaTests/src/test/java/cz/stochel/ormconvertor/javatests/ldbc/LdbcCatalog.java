package cz.stochel.ormconvertor.javatests.ldbc;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import java.util.ArrayList;
import java.util.List;

/**
 * The LDBC query catalog as the running instance serves it at {@code /ldbc} (decision 110),
 * with the binding of every Interactive query to the validation set that judges it (decision
 * 117). Read as the contract puts it on the wire - camelCase names, enums as numbers - because
 * this suite shares no type with the server (decision 078): the catalog's texts are C# content,
 * and the instance is where this suite takes them from, as it takes the artifacts.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record LdbcCatalog(int sourceOrm, List<Unit> entities, List<Query> queries) {

    /** The translation states of the catalog, as {@code LdbcTranslation} numbers them. */
    public static final int AS_SPECIFIED = 10;
    public static final int SIMPLIFIED = 20;

    public Query query(String key) {
        return queries.stream()
                .filter(query -> query.key().equals(key))
                .findFirst()
                .orElseThrow(() -> new IllegalStateException("The catalog has no query " + key + "."));
    }

    /** The units of a conversion of one query: the entity classes, then the query's text as one more unit. */
    public List<InputUnit> units(Query query) {
        List<InputUnit> units = new ArrayList<>();
        for (Unit entity : entities) {
            units.add(new InputUnit(entity.name(), entity.contentType(), entity.content()));
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
            int workload,
            int number,
            int translation,
            String sql,
            List<Parameter> parameters,
            Validation validation) {

        public Parameter parameter(String name) {
            return parameters.stream()
                    .filter(parameter -> parameter.name().equals(name))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException(key + " declares no parameter " + name + "."));
        }
    }

    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Parameter(String name, String sqlType) {
    }

    /** The binding to the judge; {@link LdbcCanonicalForm} reads it. */
    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Validation(String operation, List<Argument> arguments, List<Field> fields, boolean ordered) {
    }

    /** A parameter of the text and the field of the operation it is made from, by the numbers of {@code LdbcDerivation}. */
    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Argument(String parameter, String field, int derivation, String operand) {

        public static final int FIELD = 10;
        public static final int PLUS_DAYS = 20;
        public static final int NEXT_MONTH = 30;
    }

    /** A column of the text and the field of the result it answers to, by the numbers of {@code LdbcValueKind}. */
    @JsonIgnoreProperties(ignoreUnknown = true)
    public record Field(String column, String field, int kind, String separator, List<String> elements, String elementSeparator) {

        public static final int VALUE = 10;
        public static final int MOMENT = 20;
        public static final int DATE = 30;
        public static final int FLAG = 40;
        public static final int SET = 50;
        public static final int SEQUENCE = 60;

        /** A field that is not a list - a static factory and not a constructor, so Jackson sees one way to build a record. */
        public static Field of(String column, String field, int kind) {
            return new Field(column, field, kind, null, null, null);
        }
    }
}
