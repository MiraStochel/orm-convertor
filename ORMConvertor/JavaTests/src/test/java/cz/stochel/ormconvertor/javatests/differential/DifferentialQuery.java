package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.tool.ContentType;
import cz.stochel.ormconvertor.javatests.tool.InputUnit;
import cz.stochel.ormconvertor.javatests.tool.Orm;
import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.List;

/**
 * One query of the differential matrix: where it comes from, what a row of its result
 * looks like, and how much of a value survives rendering (decision 089). The counterpart
 * of {@code DifferentialMatrix.cs}, and the two read the same file.
 */
public record DifferentialQuery(
        String id,
        int source,
        List<String> unitPaths,
        List<String> fields,
        boolean projection,
        boolean ordered,
        ResultRow.Settings settings,
        List<Argument> arguments,
        List<String> mutations) {

    /** One parameter of a query and the value the matrix binds it to (decision 083). */
    public record Argument(String name, String typeName, String value) {

        /** The value as the generated method's parameter type wants it. */
        public Object materialize() {
            return switch (typeName) {
                case "decimal" -> new BigDecimal(value);
                case "int" -> Integer.valueOf(value);
                case "string" -> value;
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
     * The frameworks this query is paired against: every one but its own source. Its own
     * run is not a pair - it is what fixes the canonical result - but it happens all the
     * same, which is how both halves of every pair really run (decision 089).
     */
    public List<Integer> targets() {
        List<Integer> targets = new ArrayList<>();
        for (int orm : Orm.ALL) {
            if (orm != source) {
                targets.add(orm);
            }
        }

        return targets;
    }

    /** The input units of a conversion, in the order the matrix states them (decision 017). */
    public List<InputUnit> units() {
        List<InputUnit> units = new ArrayList<>();

        for (String path : unitPaths) {
            String name = path.substring(path.lastIndexOf('/') + 1);
            units.add(new InputUnit(name, ContentType.forFileName(name), DifferentialData.read("inputs/" + path)));
        }

        return units;
    }
}
