package cz.stochel.ormconvertor.javatests.tool;

/**
 * The values of {@code ConversionRecordKind} on the wire. The suite acts on one of them:
 * a {@code Failure} means the tool refused the artifact (decision 070), so a scenario
 * carrying one is over before anything is compiled - translating a refused artifact would
 * claim nothing (decision 078). The rest are asserted only where a scenario is about
 * them; the diagnostics themselves are xUnit's subject.
 */
public final class RecordKind {

    public static final int FAILURE = 1;
    public static final int LOSS = 2;
    public static final int CONVENTION = 3;
    public static final int INCOMPLETENESS = 4;
    public static final int SUPPLIED = 5;
    public static final int CONFLICT = 6;

    private RecordKind() {
    }

    public static String nameOf(int kind) {
        return switch (kind) {
            case FAILURE -> "Failure";
            case LOSS -> "Loss";
            case CONVENTION -> "Convention";
            case INCOMPLETENESS -> "Incompleteness";
            case SUPPLIED -> "Supplied";
            case CONFLICT -> "Conflict";
            default -> "kind " + kind;
        };
    }
}
