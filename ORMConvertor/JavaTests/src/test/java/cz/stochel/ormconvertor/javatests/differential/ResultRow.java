package cz.stochel.ormconvertor.javatests.differential;

import java.math.BigDecimal;
import java.math.MathContext;
import java.math.RoundingMode;
import java.sql.Timestamp;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;

/**
 * The canonical text form of one row of a query result (decision 089). The counterpart of
 * {@code ResultRow.cs}, rule for rule: both ecosystems render into this form and the
 * comparison is of the rendered text, because the two halves of a pair run in different
 * runtimes and cannot meet anywhere else.
 *
 * <p>That the two agree is not assumed. {@code renderer-conformance.txt} is rendered by
 * both suites against the same checked-in text, and it is the first thing to run: BigDecimal
 * and decimal print trailing zeros differently, LocalDateTime and DateTime format midnight
 * differently, and a tab inside a string breaks the separator if one side forgets to escape
 * it.
 *
 * <p>A type no rule names throws rather than guessing. A value rendered by a rule nobody
 * wrote would make the two suites agree or disagree for a reason no one could read out of
 * the file.
 */
public final class ResultRow {

    /** What a null field renders as. No string can collide with it: strings are quoted. */
    public static final String NULL = "NULL";

    /** Separator between fields. A tab inside a string is escaped, so it never occurs bare. */
    public static final char SEPARATOR = '\t';

    private ResultRow() {
    }

    /**
     * How much of a value survives rendering. It travels with the query in the matrix and
     * not with the host the suite runs on: an average over money bears a different scale
     * than a plain column (decision 089).
     */
    public record Settings(int decimalScale, int floatSignificantDigits, int fractionalSecondDigits) {

        public static Settings defaults() {
            return new Settings(6, 12, 3);
        }
    }

    /** One row: its fields in the order the matrix states, joined by the separator. */
    public static String render(List<Object> fields, Settings settings) {
        StringBuilder text = new StringBuilder();

        for (int index = 0; index < fields.size(); index++) {
            if (index > 0) {
                text.append(SEPARATOR);
            }
            text.append(renderField(fields.get(index), settings));
        }

        return text.toString();
    }

    /**
     * Orders rendered rows the way a query without an ordering instruction has to be
     * compared: as a set, by the rendered line itself. The .NET side sorts ordinally, which
     * is the same UTF-16 code unit order {@link String#compareTo} gives.
     */
    public static List<String> sorted(List<String> rows) {
        List<String> ordered = new ArrayList<>(rows);
        ordered.sort(String::compareTo);
        return ordered;
    }

    public static String renderField(Object value, Settings settings) {
        if (value == null) {
            return NULL;
        }

        if (value instanceof Boolean flag) {
            return flag ? "true" : "false";
        }

        // Every integral width renders the same way: an integer has no facets to lose.
        if (value instanceof Byte || value instanceof Short || value instanceof Integer || value instanceof Long) {
            return Long.toString(((Number) value).longValue());
        }

        if (value instanceof BigDecimal exact) {
            return exact.setScale(settings.decimalScale(), RoundingMode.HALF_EVEN).toPlainString();
        }

        if (value instanceof Float || value instanceof Double) {
            return renderApproximate(((Number) value).doubleValue(), settings.floatSignificantDigits());
        }

        if (value instanceof Timestamp timestamp) {
            return renderTimestamp(timestamp.toLocalDateTime(), settings.fractionalSecondDigits());
        }

        if (value instanceof LocalDateTime timestamp) {
            return renderTimestamp(timestamp, settings.fractionalSecondDigits());
        }

        if (value instanceof java.sql.Date date) {
            return date.toLocalDate().format(DateTimeFormatter.ofPattern("yyyy-MM-dd"));
        }

        if (value instanceof LocalDate date) {
            return date.format(DateTimeFormatter.ofPattern("yyyy-MM-dd"));
        }

        if (value instanceof String text) {
            return renderString(text);
        }

        throw new UnsupportedOperationException(
                "A value of type " + value.getClass().getName() + " has no rendering rule in decision 089, "
                        + "so the canonical form cannot state it. Add the rule to both suites and to the "
                        + "conformance file, or keep the type out of the matrix - do not let this guess.");
    }

    /**
     * An approximate number rounded to the stated significant digits and then written
     * without an exponent and without trailing zeros. The last step is what makes the two
     * ecosystems agree: one of them would otherwise write 3 and the other 3.0.
     */
    private static String renderApproximate(double value, int significantDigits) {
        if (Double.isNaN(value) || Double.isInfinite(value)) {
            throw new UnsupportedOperationException(
                    "The value " + value + " is not a number the canonical form can state (decision 089).");
        }

        return BigDecimal.valueOf(value)
                .round(new MathContext(significantDigits, RoundingMode.HALF_EVEN))
                .stripTrailingZeros()
                .toPlainString();
    }

    private static String renderTimestamp(LocalDateTime value, int fractionalDigits) {
        String fraction = fractionalDigits > 0 ? "." + "S".repeat(fractionalDigits) : "";
        return value.format(DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss" + fraction));
    }

    /**
     * A string in quotes, with the four characters escaped that would otherwise make one
     * field look like two, or one row like two.
     */
    private static String renderString(String value) {
        StringBuilder text = new StringBuilder(value.length() + 2);
        text.append('"');

        for (int index = 0; index < value.length(); index++) {
            char character = value.charAt(index);
            switch (character) {
                case '"' -> text.append("\\\"");
                case '\\' -> text.append("\\\\");
                case '\t' -> text.append("\\t");
                case '\n' -> text.append("\\n");
                case '\r' -> text.append("\\r");
                default -> text.append(character);
            }
        }

        text.append('"');
        return text.toString();
    }
}
