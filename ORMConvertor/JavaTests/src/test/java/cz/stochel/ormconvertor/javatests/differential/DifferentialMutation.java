package cz.stochel.ormconvertor.javatests.differential;

import java.util.ArrayList;
import java.util.List;
import java.util.function.UnaryOperator;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * The deliberately wrong translations F13 asks to be detected (decision 089). Each one is
 * applied to the <em>generated artifact</em> and not to the translator: mutating the
 * translator would be a tool of its own, with bugs of its own, and most of its mutations
 * would produce artifacts that do not even compile - which proves nothing about comparing
 * results. What is under test here is the sensitivity of the comparison, so the mutation
 * has to reach the rows.
 *
 * <p>The counterpart is {@code DifferentialMutation.cs}. The two are written twice rather
 * than shared, as the renderer is - but unlike the renderer, a drift between them cannot
 * make the suites agree wrongly: each side only ever asserts that its own mutation was
 * caught.
 */
public record DifferentialMutation(String key, String name, UnaryOperator<String> apply) {

    private static final String COUNT_PLACES = "(TOP\\s*\\(\\s*|setMaxResults\\(|FETCH NEXT )";
    private static final Pattern LITERAL_COUNT = Pattern.compile(COUNT_PLACES + "(\\d+)(\\s*\\)| ROWS)");
    private static final Pattern BOUND_COUNT = Pattern.compile(COUNT_PLACES + "(@?[A-Za-z_]\\w*|#\\{\\w+\\})(\\s*\\)| ROWS)");

    public static List<DifferentialMutation> all() {
        List<DifferentialMutation> mutations = new ArrayList<>();
        mutations.add(new DifferentialMutation("filter", "the filter is dropped", DifferentialMutation::dropFilter));
        mutations.add(new DifferentialMutation("operator", "the comparison operator is flipped", DifferentialMutation::flipOperator));
        mutations.add(new DifferentialMutation("ordering", "the ordering is dropped", DifferentialMutation::dropOrdering));
        mutations.add(new DifferentialMutation("rowCount", "the row count is changed", DifferentialMutation::changeRowCount));
        mutations.add(new DifferentialMutation("projection", "two projected fields are swapped", DifferentialMutation::swapProjection));
        return mutations;
    }

    /** The mutation a matrix entry names, or a failure that says the name is not one. */
    public static DifferentialMutation of(String key) {
        return all().stream()
                .filter(mutation -> mutation.key().equals(key))
                .findFirst()
                .orElseThrow(() -> new IllegalStateException(
                        "matrix.txt names the mutation \"" + key + "\", which is none of "
                                + all().stream().map(DifferentialMutation::key).toList() + "."));
    }

    /**
     * Every target writes its filter on a line of its own - WHERE, where. A HAVING is not a
     * filter in this sense and stays, so a query whose only condition is a HAVING carries the
     * operator mutation and not this one.
     */
    private static String dropFilter(String artifact) {
        return lines(artifact, line -> !line.matches("(?s)\\s*(WHERE|where)\\b.*") && !line.contains(".Where("));
    }

    /**
     * The comparison the filter is built on: greater against less, and greater-or-equal
     * against less-or-equal. Whitespace on both sides is what tells it from the angle
     * brackets of {@code List<Product>} and from the arrow of a lambda; without that the
     * mutation rewrites the generics and the artifact does not compile, which would prove
     * that a broken artifact differs rather than that a wrong query does. A MyBatis statement
     * lives inside XML, where the comparison is escaped, so both spellings are flipped.
     */
    private static String flipOperator(String artifact) {
        String flipped = artifact.replaceAll("(?<=\\s)&gt;=(?=\\s)", "\u0004");
        flipped = flipped.replaceAll("(?<=\\s)&lt;=(?=\\s)", "&gt;=");
        flipped = flipped.replace("\u0004", "&lt;=");

        flipped = flipped.replaceAll("(?<=\\s)&gt;(?=\\s)", "\u0002");
        flipped = flipped.replaceAll("(?<=\\s)&lt;(?=\\s)", "&gt;");
        flipped = flipped.replace("\u0002", "&lt;");

        flipped = flipped.replaceAll("(?<=\\s)>=(?=\\s)", "\u0003");
        flipped = flipped.replaceAll("(?<=\\s)<=(?=\\s)", ">=");
        flipped = flipped.replace("\u0003", "<=");

        flipped = flipped.replaceAll("(?<=\\s)>(?=\\s)", "\u0001");
        flipped = flipped.replaceAll("(?<=\\s)<(?=\\s)", ">");
        return flipped.replace('\u0001', '<');
    }

    /** Ordering lives on its own line as well - ORDER BY, order by. */
    private static String dropOrdering(String artifact) {
        return lines(artifact, line -> !line.matches("(?s).*(ORDER\\s+BY|order\\s+by)\\b.*") && !line.contains(".OrderBy"));
    }

    /**
     * The row count of a paginated query, wherever the target put it: inside TOP, after
     * FETCH NEXT, on the query object. A literal count becomes one less, a bound one - a
     * parameter of the generated method (decision 085) - becomes itself plus one, an
     * expression every one of those places accepts; either way the slice is another slice
     * of the same ordered rows.
     */
    private static String changeRowCount(String artifact) {
        Matcher literal = LITERAL_COUNT.matcher(artifact);
        StringBuilder changed = new StringBuilder();
        while (literal.find()) {
            literal.appendReplacement(changed, Matcher.quoteReplacement(
                    literal.group(1) + (Integer.parseInt(literal.group(2)) - 1) + literal.group(3)));
        }
        literal.appendTail(changed);

        return BOUND_COUNT.matcher(changed.toString()).replaceAll("$1$2 + 1$3");
    }

    /**
     * The two projected expressions swapped under their own aliases: the row keeps its shape
     * and every field holds the other one's value. Over the fixture no two projected fields
     * of a query agree in every row, so nothing survives the swap by coincidence.
     */
    private static String swapProjection(String artifact) {
        return artifact.replaceAll(
                "(\\w+\\.)(\\w+)(\\s+(?:AS|as)\\s+)(\\w+)(,\\s*)(\\w+\\.)(\\w+)(\\s+(?:AS|as)\\s+)(\\w+)",
                "$1$7$3$4$5$6$2$8$9");
    }

    /**
     * The artifact without the lines a rule rejects - and the artifact itself when it
     * rejects none. Returning a rebuilt text either way would make every no-op look like a
     * mutation, because rebuilding normalizes the line endings; a query with no ordering
     * would then be reported as having lost one.
     */
    private static String lines(String artifact, java.util.function.Predicate<String> keep) {
        String[] all = artifact.replace("\r\n", "\n").split("\n", -1);
        List<String> kept = new ArrayList<>();

        for (String line : all) {
            if (keep.test(line)) {
                kept.add(line);
            }
        }

        return kept.size() == all.length ? artifact : String.join(System.lineSeparator(), kept);
    }
}
