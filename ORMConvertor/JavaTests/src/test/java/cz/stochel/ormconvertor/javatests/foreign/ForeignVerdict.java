package cz.stochel.ormconvertor.javatests.foreign;

import java.io.IOException;
import java.io.UncheckedIOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Collection;
import java.util.Locale;
import java.util.Map;
import java.util.TreeMap;

/**
 * What became of one foreign artifact (decision 172) - the counterpart of
 * {@code ForeignVerdict.cs}: the highest level it reached, the outcome at the level it stopped
 * at, the diagnostic of that level, and the provenance the manifest stated, copied. One line of
 * the file of rows; both suites write the same columns, which the .NET suite holds equal to
 * {@link #HEADER} by reading this source.
 */
public final class ForeignVerdict {

    public static final String HEADER = "artifact\tanswers\tsource\ttarget\tproducer\tlanguage\treached\toutcome\tdetail\trows\tagrees\treadMs\tcompileMs\tacceptMs\trunMs\tmetadata";

    /** The levels of decision 016 as the row counts them. */
    public static final int LEVEL_NONE = 0;
    public static final int LEVEL_READ = 1;
    public static final int LEVEL_COMPILED = 2;
    public static final int LEVEL_ACCEPTED = 3;
    public static final int LEVEL_RAN = 4;

    public static final String IN_PROGRESS = "in progress";

    /** The outcome of a translation the tool refused with a Failure record, so that there is no artifact to judge (decision 173); the row of the experiment run alone. */
    public static final String REFUSED = "refused";

    public static final String UNREADABLE = "unreadable";
    public static final String NOT_COMPILED = "not compiled";
    public static final String NOT_ACCEPTED = "not accepted";
    public static final String FAILED = "failed";
    public static final String DIFFERS = "differs";
    public static final String AGREES = "agrees";

    /**
     * The outcome of a project whose every query ran over the empty tables of its schema
     * (decision 176): runnable (T3), with no measure of equivalence to agree with, so
     * {@code agrees} stays empty.
     */
    public static final String RAN = "ran";

    /** The outcome of an artifact that has no fourth level: no query to run, or a key whose measure is a replay (LDBC). */
    public static final String ACCEPTED = "accepted";

    private final String artifact;
    private final String answers;
    private final String source;
    private final String target;
    private final String producer;
    private final String language;
    private final String metadata;

    private int reached = LEVEL_NONE;
    private String outcome = IN_PROGRESS;
    private String detail = "";
    private Integer rows;
    private Boolean agrees;
    private double readMs;
    private double compileMs;
    private double acceptMs;
    private double runMs;

    public ForeignVerdict(String artifact, String answers, String source, String target, String producer, String language, String metadata) {
        this.artifact = artifact;
        this.answers = answers;
        this.source = source;
        this.target = target;
        this.producer = producer;
        this.language = language;
        this.metadata = metadata;
    }

    /** The row of a translation the tool refused (decision 173): level 0, outcome refused, the reasons as the detail. */
    public static ForeignVerdict refused(String artifact, String answers, String source, String target, String producer, String reasons, String metadata) {
        ForeignVerdict verdict = new ForeignVerdict(artifact, answers, source, target, producer, ForeignArtifact.LANGUAGE_REFUSED, metadata);
        verdict.stop(REFUSED, reasons);
        return verdict;
    }

    public String language() {
        return language;
    }

    public String artifact() {
        return artifact;
    }

    public int reached() {
        return reached;
    }

    public String outcome() {
        return outcome;
    }

    public String detail() {
        return detail;
    }

    public Integer rows() {
        return rows;
    }

    public Boolean agrees() {
        return agrees;
    }

    void reached(int level) {
        this.reached = level;
    }

    void stop(String outcome, String detail) {
        this.outcome = outcome;
        this.detail = detail == null ? "" : detail;
    }

    void ran(int rowCount, boolean agreed, String difference) {
        this.reached = LEVEL_RAN;
        this.rows = rowCount;
        this.agrees = agreed;
        this.outcome = agreed ? AGREES : DIFFERS;
        this.detail = agreed ? "" : difference;
    }

    /** A project ran every query over the empty tables of its schema (decision 176): the fourth level without a measure. */
    void ran(int rowCount, String detail) {
        this.reached = LEVEL_RAN;
        this.rows = rowCount;
        this.agrees = null;
        this.outcome = RAN;
        this.detail = detail == null ? "" : detail;
    }

    void times(double read, double compile, double accept, double run) {
        this.readMs = read;
        this.compileMs = compile;
        this.acceptMs = accept;
        this.runMs = run;
    }

    public String row() {
        return String.join("\t",
                artifact, answers, source, target, producer, language,
                String.valueOf(reached),
                outcome,
                clean(detail),
                rows == null ? "" : String.valueOf(rows),
                agrees == null ? "" : agrees ? "1" : "0",
                String.format(Locale.ROOT, "%.0f", readMs),
                String.format(Locale.ROOT, "%.0f", compileMs),
                String.format(Locale.ROOT, "%.0f", acceptMs),
                String.format(Locale.ROOT, "%.0f", runMs),
                clean(metadata));
    }

    public String describe() {
        return artifact + " (" + answers + ", " + source + " -> " + target + ", " + producer + ", " + language + "): level " + reached + ", " + outcome
                + (rows != null ? ", " + rows + " rows" : "")
                + (detail.isEmpty() ? "" : ": " + detail);
    }

    private static String clean(String text) {
        return text.replace('\t', ' ').replace("\r", "").replace('\n', ' ');
    }

    /** The file of rows: one line per artifact under the header, rewritten whole on every change so that a run stopped from outside leaves the rows it had. */
    public static void write(Path directory, String fileName, Collection<ForeignVerdict> verdicts) {
        write(directory, fileName, verdicts, Map.of());
    }

    /** The same, with rows of an earlier run kept beside the verdicts: lines of the file by their artifact, which a verdict of the same artifact replaces. */
    public static void write(Path directory, String fileName, Collection<ForeignVerdict> verdicts, Map<String, String> kept) {
        TreeMap<String, String> ordered = new TreeMap<>(kept);
        for (ForeignVerdict verdict : verdicts) {
            ordered.put(verdict.artifact, verdict.row());
        }

        StringBuilder text = new StringBuilder(HEADER).append('\n');
        for (String row : ordered.values()) {
            text.append(row).append('\n');
        }

        try {
            Files.createDirectories(directory);
            Files.writeString(directory.resolve(fileName), text.toString(), StandardCharsets.UTF_8);
        } catch (IOException e) {
            throw new UncheckedIOException("The rows could not be written into " + directory + ".", e);
        }
    }
}
