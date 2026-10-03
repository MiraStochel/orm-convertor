package cz.stochel.ormconvertor.javatests.ldbc;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import cz.stochel.ormconvertor.javatests.differential.ResultRow;
import java.sql.Timestamp;
import java.time.Instant;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.LocalTime;
import java.time.ZoneOffset;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * The validation set of LDBC Interactive v1 and the rows of a generated artifact, put into the
 * one canonical form of decision 089 so that they meet (decision 117) - the counterpart of
 * {@code LdbcCanonicalForm.cs}, rule for rule, and held to it by the rows both suites render
 * into {@code renderer-conformance.txt}:
 * <ul>
 * <li>a moment is milliseconds since the epoch in UTC, a date the milliseconds of its midnight,
 * a flag a truth value the text writes as 1 or 0;</li>
 * <li>a set is a list compared as the set of its elements: they are joined with the separator
 * in the order of {@link String#compareTo}, which is the ordinal order of the .NET side, an
 * element that is an object as the values of the named fields joined with the element
 * separator; an empty list is NULL, as the STRING_AGG of the text over no rows is;</li>
 * <li>a result that is one object is a result of one row.</li>
 * </ul>
 * A JSON value the kind does not say how to read throws, as the renderer does for a type it
 * has no rule for.
 */
public final class LdbcCanonicalForm {

    private static final ObjectMapper MAPPER = new ObjectMapper();
    private static final long MILLISECONDS_PER_DAY = 86_400_000L;
    private static final ResultRow.Settings SETTINGS = ResultRow.Settings.defaults();

    private LdbcCanonicalForm() {
    }

    /** The expected result of a read, as the canonical rows of the binding's columns. */
    public static List<String> expected(LdbcCatalog.Validation binding, String result) {
        JsonNode root = parse(result);

        List<JsonNode> rows = new ArrayList<>();
        if (root.isObject()) {
            rows.add(root);
        } else if (root.isArray()) {
            root.forEach(rows::add);
        } else {
            throw new UnsupportedOperationException(
                    binding.operation() + ": the expected result is a JSON " + root.getNodeType() + ", neither a row nor a list of rows.");
        }

        List<String> rendered = new ArrayList<>();
        for (JsonNode row : rows) {
            List<Object> values = new ArrayList<>();
            for (LdbcCatalog.Field field : binding.fields()) {
                values.add(expected(field, member(binding, row, field.field())));
            }
            rendered.add(ResultRow.render(values, SETTINGS));
        }

        return binding.ordered() ? rendered : ResultRow.sorted(rendered);
    }

    /** The rows a generated artifact returned, with each column read as the binding's kind says. */
    public static List<String> actual(LdbcCatalog.Validation binding, List<List<Object>> rows) {
        List<String> rendered = new ArrayList<>();
        for (List<Object> row : rows) {
            List<Object> values = new ArrayList<>();
            for (int index = 0; index < binding.fields().size(); index++) {
                values.add(actual(binding.fields().get(index), row.get(index)));
            }
            rendered.add(ResultRow.render(values, SETTINGS));
        }

        return binding.ordered() ? rendered : ResultRow.sorted(rendered);
    }

    /** One field of the expected result as the value the renderer states. */
    public static Object expected(LdbcCatalog.Field field, JsonNode value) {
        if (value.isNull()) {
            return null;
        }

        return switch (field.kind()) {
            case LdbcCatalog.Field.VALUE -> scalar(field, value);
            case LdbcCatalog.Field.MOMENT -> moment(integer(field, value));
            case LdbcCatalog.Field.DATE -> date(field, integer(field, value));
            case LdbcCatalog.Field.FLAG -> {
                if (!value.isBoolean()) {
                    throw unreadable(field, value);
                }
                yield value.booleanValue() ? 1L : 0L;
            }
            case LdbcCatalog.Field.SET -> list(field, value, false);
            case LdbcCatalog.Field.SEQUENCE -> list(field, value, true);
            default -> throw new UnsupportedOperationException(
                    field.column() + ": the kind " + field.kind() + " has no rule (decision 117).");
        };
    }

    /**
     * One column of a row a generated artifact returned, read as the binding's kind says: a
     * date a framework hands back as a moment at midnight is that date, a truth value is the
     * number the text writes for it, and a joined list is the set of its elements. Values,
     * not types (decision 089).
     */
    public static Object actual(LdbcCatalog.Field field, Object value) {
        if (value == null) {
            return null;
        }

        if (field.kind() == LdbcCatalog.Field.DATE) {
            if (value instanceof Timestamp timestamp) {
                value = timestamp.toLocalDateTime();
            }
            if (value instanceof LocalDateTime moment && moment.toLocalTime().equals(LocalTime.MIDNIGHT)) {
                return moment.toLocalDate();
            }
        }

        if (field.kind() == LdbcCatalog.Field.FLAG && value instanceof Boolean flag) {
            return flag ? 1L : 0L;
        }

        if (field.kind() == LdbcCatalog.Field.SET && value instanceof String joined) {
            List<String> elements = new ArrayList<>(Arrays.asList(joined.split(java.util.regex.Pattern.quote(field.separator()), -1)));
            elements.sort(String::compareTo);
            return String.join(field.separator(), elements);
        }

        return value;
    }

    /**
     * The arguments of a read, in the order of the parameters of the generated method: each
     * made from the fields of the operation as the binding says, typed as the parameter of the
     * text declares it - a number, a text, or a moment from the milliseconds the driver writes.
     */
    public static Object[] arguments(LdbcCatalog.Query query, List<String> parameterNames, String operation) {
        JsonNode root = parse(operation);
        Object[] arguments = new Object[parameterNames.size()];

        for (int index = 0; index < arguments.length; index++) {
            String name = parameterNames.get(index);
            LdbcCatalog.Argument argument = query.validation().arguments().stream()
                    .filter(candidate -> candidate.parameter().equals(name))
                    .findFirst()
                    .orElseThrow(() -> new IllegalStateException(query.key() + ": the generated method takes the parameter "
                            + name + ", which the binding of " + query.validation().operation() + " does not bind."));

            arguments[index] = argument(query.key(), argument, query.parameter(name).sqlType(), root);
        }

        return arguments;
    }

    private static Object argument(String key, LdbcCatalog.Argument argument, String sqlType, JsonNode operation) {
        JsonNode field = field(key, operation, argument.field());

        Object value = switch (argument.derivation()) {
            case LdbcCatalog.Argument.FIELD -> field.isTextual() ? field.textValue() : (Object) field.longValue();
            case LdbcCatalog.Argument.PLUS_DAYS -> field.longValue() + field(key, operation, argument.operand()).longValue() * MILLISECONDS_PER_DAY;
            case LdbcCatalog.Argument.NEXT_MONTH -> field.longValue() % 12 + 1;
            default -> throw new UnsupportedOperationException(key + ": the derivation " + argument.derivation() + " has no rule (decision 117).");
        };

        String type = sqlType.split("\\(")[0].toUpperCase(java.util.Locale.ROOT);
        return switch (type) {
            case "BIGINT" -> ((Number) value).longValue();
            case "INT" -> ((Number) value).intValue();
            case "NVARCHAR", "VARCHAR" -> (String) value;
            case "DATE", "DATETIME2" -> moment(((Number) value).longValue());
            default -> throw new UnsupportedOperationException(key + ": no binding for a parameter of type " + sqlType + " (decision 117).");
        };
    }

    private static JsonNode field(String key, JsonNode operation, String name) {
        JsonNode value = operation.get(name);
        if (value == null) {
            throw new IllegalStateException(key + ": the operation has no field \"" + name + "\": " + operation);
        }
        return value;
    }

    private static JsonNode member(LdbcCatalog.Validation binding, JsonNode row, String name) {
        JsonNode value = row.get(name);
        if (value == null) {
            throw new IllegalStateException(
                    binding.operation() + ": a row of the expected result has no field \"" + name + "\": " + row);
        }
        return value;
    }

    private static Object scalar(LdbcCatalog.Field field, JsonNode value) {
        if (value.isTextual()) {
            return value.textValue();
        }
        if (value.isIntegralNumber()) {
            return value.longValue();
        }
        if (value.isNumber()) {
            return value.doubleValue();
        }
        throw unreadable(field, value);
    }

    private static long integer(LdbcCatalog.Field field, JsonNode value) {
        if (!value.isIntegralNumber()) {
            throw unreadable(field, value);
        }
        return value.longValue();
    }

    private static LocalDateTime moment(long milliseconds) {
        return LocalDateTime.ofInstant(Instant.ofEpochMilli(milliseconds), ZoneOffset.UTC);
    }

    private static LocalDate date(LdbcCatalog.Field field, long milliseconds) {
        if (milliseconds % MILLISECONDS_PER_DAY != 0) {
            throw new UnsupportedOperationException(
                    field.column() + ": " + milliseconds + " is not the midnight of a day, so it is not a date (decision 117).");
        }
        return LocalDate.ofEpochDay(milliseconds / MILLISECONDS_PER_DAY);
    }

    private static String list(LdbcCatalog.Field field, JsonNode value, boolean ordered) {
        if (!value.isArray()) {
            throw unreadable(field, value);
        }

        List<String> elements = new ArrayList<>();
        for (JsonNode element : value) {
            elements.add(element(field, element));
        }

        if (elements.isEmpty()) {
            return null;
        }

        if (!ordered) {
            elements.sort(String::compareTo);
        }

        return String.join(field.separator(), elements);
    }

    private static String element(LdbcCatalog.Field field, JsonNode element) {
        if (element.isTextual()) {
            return element.textValue();
        }
        if (element.isIntegralNumber()) {
            return Long.toString(element.longValue());
        }
        if (element.isObject() && field.elements() != null) {
            List<String> parts = new ArrayList<>();
            for (String name : field.elements()) {
                JsonNode part = element.get(name);
                if (part == null) {
                    throw new IllegalStateException(field.column() + ": an element has no field \"" + name + "\": " + element);
                }
                parts.add(element(new LdbcCatalog.Field(field.column(), field.field(), field.kind(), field.separator(), null, null), part));
            }
            return String.join(field.elementSeparator(), parts);
        }
        throw unreadable(field, element);
    }

    private static UnsupportedOperationException unreadable(LdbcCatalog.Field field, JsonNode value) {
        return new UnsupportedOperationException(
                field.column() + ": the kind " + field.kind() + " has no rule for the JSON " + value.getNodeType() + " " + value + " (decision 117).");
    }

    private static JsonNode parse(String json) {
        try {
            return MAPPER.readTree(json);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException("The validation set holds JSON that does not parse: " + json, e);
        }
    }

    /** Rows from arrays, for the conformance rows of the actual side. */
    static List<List<Object>> rows(Object[]... rows) {
        List<List<Object>> list = new ArrayList<>();
        for (Object[] row : rows) {
            list.add(Arrays.asList(row));
        }
        return list;
    }
}
