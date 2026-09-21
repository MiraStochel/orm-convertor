package cz.stochel.ormconvertor.javatests.differential;

import java.util.ArrayList;
import java.util.List;
import java.util.function.UnaryOperator;

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

    /** Every target writes its filter on a line of its own - WHERE, where, .Where(. */
    private static String dropFilter(String artifact) {
        return lines(artifact, line -> !line.matches("(?s)\\s*(WHERE|where)\\b.*") && !line.contains(".Where("));
    }

    /**
     * The comparison the filter is built on. Whitespace on both sides is what tells it from
     * the angle brackets of {@code List<Product>} and from the arrow of a lambda; without
     * that the mutation rewrites the generics and the artifact does not compile, which would
     * prove that a broken artifact differs rather than that a wrong query does.
     */
    private static String flipOperator(String artifact) {
        // A MyBatis statement lives inside XML, where the comparison is escaped, so both
        // spellings have to be flipped or the mutation would never reach that one target.
        String flipped = artifact.replaceAll("(?<=\\s)&gt;(?=\\s)", "\u0002");
        flipped = flipped.replaceAll("(?<=\\s)&lt;(?=\\s)", "&gt;");
        flipped = flipped.replace("\u0002", "&lt;");

        flipped = flipped.replaceAll("(?<=\\s)>(?=\\s)", "\u0001");
        flipped = flipped.replaceAll("(?<=\\s)<(?=\\s)", ">");
        return flipped.replace('\u0001', '<');
    }

    /** Ordering lives on its own line as well - ORDER BY, order by, .OrderBy (descending included). */
    private static String dropOrdering(String artifact) {
        return lines(artifact, line -> !line.matches("(?s).*(ORDER\\s+BY|order\\s+by)\\b.*") && !line.contains(".OrderBy"));
    }

    /**
     * The row count of a paginated query, wherever the target put it: inside TOP or on the
     * query object. Three of six rows is a proper prefix, so two is a different answer
     * whichever way the target slices.
     */
    private static String changeRowCount(String artifact) {
        return artifact.replaceAll("(TOP\\s*\\(\\s*|setMaxResults\\()3(\\s*\\))", "$12$2");
    }

    /**
     * The two projected expressions swapped under their own aliases: the row keeps its shape
     * and every field holds the other one's value. Over this fixture Sku and ProductName
     * differ in every row, so nothing survives the swap by coincidence.
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
