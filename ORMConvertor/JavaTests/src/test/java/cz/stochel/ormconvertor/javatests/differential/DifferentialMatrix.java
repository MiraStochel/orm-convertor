package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.shapes.QueryCategories;
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

    /** Every (query, source, target) the matrix states and no target refuses - the pairs of F13. */
    public static List<Pair> pairs() {
        List<Pair> pairs = new ArrayList<>();
        for (DifferentialQuery query : queries()) {
            for (int source : query.sources()) {
                for (int target : query.targets(source)) {
                    pairs.add(new Pair(query.id(), source, target));
                }
            }
        }

        return pairs;
    }

    /**
     * Every direction the matrix states as refused. Decision 089 wants a refused direction
     * said in that word rather than left out, and a suite asserts that the refusal really
     * happens, so a target that starts accepting the query is noticed.
     */
    public static List<RefusedDirection> refusedDirections() {
        List<RefusedDirection> refused = new ArrayList<>();
        for (DifferentialQuery query : queries()) {
            for (int source : query.sources()) {
                for (Map.Entry<Integer, Integer> refusal : query.refusedBy().entrySet()) {
                    if (refusal.getKey() != source) {
                        refused.add(new RefusedDirection(query.id(), source, refusal.getKey(), refusal.getValue()));
                    }
                }
            }
        }

        return refused;
    }

    /** One pair of the criterion of F13: a source variant of a query against one translation of it. */
    public record Pair(String queryId, int source, int target) {
    }

    /** A direction the matrix states as refused: the target's descriptor cannot express the query (decision 053). */
    public record RefusedDirection(String queryId, int source, int target, int feature) {
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

        // A query is either the matrix's own - one source and its units - or a category of
        // the manifest, whose sources and units the manifest states. Stating both would be
        // two answers to one question, so it is refused.
        String category = values.get("category");
        List<Integer> sources;
        List<String> unitPaths;
        Map<Integer, Integer> refusedBy;

        if (category != null) {
            if (values.containsKey("source") || values.containsKey("units")) {
                throw new IllegalStateException(
                        "matrix.txt: [" + id + "] names the category " + category + " and states a source or units of its own; the manifest states those.");
            }

            QueryCategories.Category manifest = QueryCategories.byId(category);

            // A source the manifest lists under refusedFrom is no source here either: its own
            // run is the identity direction, where a Dapper target demands no mapping fact
            // and the catalog supplies no table to type the parameter from (decision 083),
            // and a query whose source variant cannot run is no query of the matrix (089).
            List<Integer> stating = new ArrayList<>();
            for (int orm : Orm.ALL) {
                if (manifest.statedBy(orm) && manifest.refusalFrom(orm) == null) {
                    stating.add(orm);
                }
            }

            sources = List.copyOf(stating);
            unitPaths = List.of();
            refusedBy = manifest.refusedBy();
        } else {
            sources = List.of(Orm.forName(required(id, values, "source")));
            unitPaths = list(required(id, values, "units"));
            refusedBy = Map.of();
        }

        return new DifferentialQuery(
                id,
                category,
                sources,
                unitPaths,
                list(required(id, values, "fields")),
                shape.equals("projection"),
                Boolean.parseBoolean(required(id, values, "ordered")),
                new ResultRow.Settings(
                        number(values, "decimalScale", 6),
                        number(values, "floatDigits", 12),
                        number(values, "fractionalSeconds", 3)),
                values.containsKey("arguments") ? arguments(values.get("arguments")) : List.of(),
                list(required(id, values, "mutations")),
                refusedBy);
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
