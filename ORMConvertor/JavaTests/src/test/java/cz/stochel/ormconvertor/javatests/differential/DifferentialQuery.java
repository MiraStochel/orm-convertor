package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.shapes.QueryCategories;
import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * One query of the differential matrix: who states it, what a row of its result looks
 * like, and how much of a value survives rendering (decision 089). The counterpart of
 * {@code DifferentialMatrix.cs}, and the two read the same file.
 *
 * <p>A query has one canonical result and one or more sources. The six queries the matrix
 * began with each have one source of their own; a category of T2 is stated by every source
 * the shared manifest lists for it ({@code QueryShapes/categories.txt}), and every one of
 * those source variants runs against the same canonical file - so the comparison is n-way
 * across sources as much as across targets.
 *
 * @param id       the section of {@code matrix.txt}, and the name of the canonical result
 * @param category the category of the manifest this query is, or null for a query of the matrix's own
 * @param sources  the frameworks that state the query, in the order the file lists them
 * @param unitPaths the input units of a query of the matrix's own, under {@code inputs/}; empty for a category
 * @param refusedBy targets that refuse the query, with the feature the refusal names (decision 053)
 * @param fallbackBy targets that write the query in native SQL because their query language does not speak it,
 *                   with the feature the record of kind Fallback names (decision 113) - pairs like any other
 */
public record DifferentialQuery(
        String id,
        String category,
        List<Integer> sources,
        List<String> unitPaths,
        List<String> fields,
        boolean projection,
        boolean ordered,
        ResultRow.Settings settings,
        List<Argument> arguments,
        List<String> mutations,
        Map<Integer, Integer> refusedBy,
        Map<Integer, Integer> fallbackBy) {

    /** One parameter of a query and the value the matrix binds it to (decision 083). */
    public record Argument(String name, String typeName, String value) {

        /**
         * The value as the generated method's parameter type wants it. A collection is
         * written as its elements separated by semicolons, because the argument list itself
         * is separated by commas.
         */
        public Object materialize() {
            return switch (typeName) {
                case "decimal" -> new BigDecimal(value);
                case "int" -> Integer.valueOf(value);
                case "long" -> Long.valueOf(value);
                case "string" -> value;
                case "int[]" -> {
                    List<Integer> elements = new ArrayList<>();
                    for (String element : value.split(";")) {
                        if (!element.isBlank()) {
                            elements.add(Integer.valueOf(element.strip()));
                        }
                    }
                    yield elements;
                }
                default -> throw new UnsupportedOperationException(
                        "The matrix binds " + name + " to a value of type \"" + typeName + "\", which no suite "
                                + "knows how to make. Add the type to both suites or state the argument differently.");
            };
        }
    }

    /** The canonical result of this query, as the file beside the matrix states it. */
    public List<String> canonicalResult() {
        return DifferentialData.readLines("results/" + id + ".txt");
    }

    /**
     * The frameworks a source of this query is paired against: every one but the source
     * itself and but a target that refuses the query - a target that falls back to native
     * SQL included (decision 113). The source's own run
     * is not a pair - it is what fixes the canonical result - but it happens all the same,
     * which is how both halves of every pair really run (decision 089).
     */
    public List<Integer> targets(int source) {
        List<Integer> targets = new ArrayList<>();
        for (int orm : Orm.ALL) {
            if (orm != source && !refusedBy.containsKey(orm)) {
                targets.add(orm);
            }
        }

        return targets;
    }

    /**
     * The input units of a conversion from the source, in the order they are sent (decision
     * 017): for a category the shared domain of five entities and the category's query units,
     * for a query of the matrix's own the files the matrix lists under {@code inputs/}.
     */
    public List<InputUnit> units(int source) {
        if (!sources.contains(source)) {
            throw new IllegalArgumentException(id + " is not stated by " + Orm.nameOf(source) + ".");
        }

        if (category != null) {
            return QueryCategories.byId(category).input(source);
        }

        List<InputUnit> units = new ArrayList<>();
        for (String path : unitPaths) {
            String name = path.substring(path.lastIndexOf('/') + 1);
            units.add(new InputUnit(name, ContentType.forFileName(name), DifferentialData.read("inputs/" + path)));
        }

        return units;
    }
}
