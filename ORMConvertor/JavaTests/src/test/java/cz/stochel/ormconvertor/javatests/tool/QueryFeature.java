package cz.stochel.ormconvertor.javatests.tool;

/**
 * The values of the tool's {@code QueryFeature} on the wire - the category a refusal of a
 * query names in its record (decision 053) - as numbers, which is how the REST contract
 * puts an enum there (decision 043). The suite carries them as constants for the reason
 * {@link Orm} does: a process in another runtime has no other way to them.
 *
 * <p>The shared manifest of the query categories names a feature by its enum name, so
 * that the .NET suite and this one read one text; this table is what turns the name into
 * the number a record carries.
 */
public final class QueryFeature {

    public static final int PROJECTION = 1;
    public static final int FILTERING = 2;
    public static final int JOIN = 3;
    public static final int JOIN_KIND = 4;
    public static final int AGGREGATION = 5;
    public static final int GROUPING = 6;
    public static final int POST_AGGREGATION_FILTERING = 7;
    public static final int ORDERING = 8;
    public static final int PAGINATION = 9;
    public static final int SUBQUERY = 10;
    public static final int SET_OPERATION = 11;
    public static final int QUERY_PARAMETER = 12;
    public static final int INTERMEDIATE_RESULT = 14;
    public static final int RECURSION = 15;

    private static final int[] ALL = {
        PROJECTION, FILTERING, JOIN, JOIN_KIND, AGGREGATION, GROUPING, POST_AGGREGATION_FILTERING,
        ORDERING, PAGINATION, SUBQUERY, SET_OPERATION, QUERY_PARAMETER, INTERMEDIATE_RESULT, RECURSION,
    };

    private QueryFeature() {
    }

    /** The feature a name stands for, spelled as the tool's enum spells it; an unknown name stops the run. */
    public static int forName(String name) {
        for (int feature : ALL) {
            if (nameOf(feature).equals(name)) {
                return feature;
            }
        }

        throw new IllegalArgumentException("No query feature is named \"" + name + "\".");
    }

    public static String nameOf(int feature) {
        return switch (feature) {
            case PROJECTION -> "Projection";
            case FILTERING -> "Filtering";
            case JOIN -> "Join";
            case JOIN_KIND -> "JoinKind";
            case AGGREGATION -> "Aggregation";
            case GROUPING -> "Grouping";
            case POST_AGGREGATION_FILTERING -> "PostAggregationFiltering";
            case ORDERING -> "Ordering";
            case PAGINATION -> "Pagination";
            case SUBQUERY -> "Subquery";
            case SET_OPERATION -> "SetOperation";
            case QUERY_PARAMETER -> "QueryParameter";
            case INTERMEDIATE_RESULT -> "IntermediateResult";
            case RECURSION -> "Recursion";
            default -> "feature " + feature;
        };
    }
}
