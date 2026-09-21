package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Reads {@code matrix.txt}. Its counterpart is {@code DifferentialMatrix.cs}, and the
 * format is poorer than JSON on purpose: two parsers in two languages have to agree about
 * it, and a line of "key = value" cannot be read two ways.
 */
public final class DifferentialMatrix {

    /** What the criterion of F13 asks of the matrix, asserted by the suite rather than by a document. */
    public static final int REQUIRED_PAIRS = 30;

    private static List<DifferentialQuery> cached;

    private DifferentialMatrix() {
    }

    public static synchronized List<DifferentialQuery> queries() {
        if (cached == null) {
            cached = parse();
        }

        return cached;
    }

    public static DifferentialQuery query(String id) {
        return queries().stream()
                .filter(query -> query.id().equals(id))
                .findFirst()
                .orElseThrow(() -> new IllegalArgumentException("The matrix states no query \"" + id + "\"."));
    }

    /** Every (query, target) the matrix states, which is what a pair of F13 is. */
    public static List<Pair> pairs() {
        List<Pair> pairs = new ArrayList<>();
        for (DifferentialQuery query : queries()) {
            for (int target : query.targets()) {
                pairs.add(new Pair(query.id(), target));
            }
        }

        return pairs;
    }

    public record Pair(String queryId, int target) {
    }

    private static List<DifferentialQuery> parse() {
        List<DifferentialQuery> queries = new ArrayList<>();
        String id = null;
        Map<String, String> values = new LinkedHashMap<>();

        for (String raw : DifferentialData.readLines("matrix.txt")) {
            String line = raw.strip();

            if (line.isEmpty() || line.startsWith("#")) {
                continue;
            }

            if (line.startsWith("[") && line.endsWith("]")) {
                if (id != null) {
                    queries.add(build(id, values));
                }

                id = line.substring(1, line.length() - 1).strip();
                values = new LinkedHashMap<>();
                continue;
            }

            int separator = line.indexOf('=');
            if (separator < 0) {
                throw new IllegalStateException("matrix.txt: \"" + line + "\" is neither a section nor a key.");
            }

            values.put(line.substring(0, separator).strip(), line.substring(separator + 1).strip());
        }

        if (id != null) {
            queries.add(build(id, values));
        }

        return List.copyOf(queries);
    }

    private static DifferentialQuery build(String id, Map<String, String> values) {
        String shape = required(id, values, "shape");
        if (!shape.equals("entity") && !shape.equals("projection")) {
            throw new IllegalStateException("matrix.txt: [" + id + "] has shape \"" + shape + "\", which is neither.");
        }

        return new DifferentialQuery(
                id,
                Orm.forName(required(id, values, "source")),
                list(required(id, values, "units")),
                list(required(id, values, "fields")),
                shape.equals("projection"),
                Boolean.parseBoolean(required(id, values, "ordered")),
                new ResultRow.Settings(
                        number(values, "decimalScale", 6),
                        number(values, "floatDigits", 12),
                        number(values, "fractionalSeconds", 3)),
                values.containsKey("arguments") ? arguments(values.get("arguments")) : List.of(),
                list(required(id, values, "mutations")));
    }

    private static String required(String id, Map<String, String> values, String key) {
        String value = values.get(key);
        if (value == null) {
            throw new IllegalStateException("matrix.txt: [" + id + "] states no \"" + key + "\".");
        }

        return value;
    }

    private static int number(Map<String, String> values, String key, int fallback) {
        return values.containsKey(key) ? Integer.parseInt(values.get(key)) : fallback;
    }

    private static List<String> list(String value) {
        List<String> entries = new ArrayList<>();
        for (String entry : value.split(",")) {
            String trimmed = entry.strip();
            if (!trimmed.isEmpty()) {
                entries.add(trimmed);
            }
        }

        return entries;
    }

    private static List<DifferentialQuery.Argument> arguments(String value) {
        List<DifferentialQuery.Argument> arguments = new ArrayList<>();

        for (String entry : list(value)) {
            int colon = entry.indexOf(':');
            int equals = entry.indexOf('=');

            if (colon < 0 || equals < colon) {
                throw new IllegalStateException(
                        "matrix.txt: the argument \"" + entry + "\" is not written as name:type=value.");
            }

            arguments.add(new DifferentialQuery.Argument(
                    entry.substring(0, colon).strip(),
                    entry.substring(colon + 1, equals).strip(),
                    entry.substring(equals + 1).strip()));
        }

        return arguments;
    }
}
