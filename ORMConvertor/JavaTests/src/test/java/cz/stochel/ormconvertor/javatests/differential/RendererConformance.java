package cz.stochel.ormconvertor.javatests.differential;

import cz.stochel.ormconvertor.javatests.ldbc.LdbcConformance;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * The rows both ecosystems render to prove they render alike (decision 089). The list is
 * code rather than data on purpose: every entry has to be expressible in both languages,
 * and a value written into a file would only prove that both suites can read a file.
 *
 * <p>The counterpart is {@code RendererConformance.cs}; the expected text is
 * {@code renderer-conformance.txt} beside the schema script, checked in rather than
 * recorded, so neither suite can move the target by re-running itself.
 */
public final class RendererConformance {

    private RendererConformance() {
    }

    public static ResultRow.Settings settings() {
        return ResultRow.Settings.defaults();
    }

    public static List<List<Object>> rows() {
        return List.of(
                row((Object) null),
                row(Boolean.TRUE),
                row(Boolean.FALSE),
                row(0),
                row(-42),
                row(9007199254740993L),

                // Exact decimals: the stated scale is kept even when the value has fewer
                // digits, and a tie is resolved half to even in both directions - the digit
                // before the tie is even in the first case and odd in the second.
                row(new BigDecimal("0")),
                row(new BigDecimal("1250.5")),
                row(new BigDecimal("1.0000005")),
                row(new BigDecimal("1.0000015")),

                // Approximate numbers: trailing zeros go, so that one ecosystem cannot write
                // 3.0 where the other writes 3. Every value here is exact in binary floating
                // point, so the case under test is the rendering and not the arithmetic.
                row(3.0d),
                row(2.5d),
                row(0.5d),
                row(-1.75d),
                row(0.0d),

                row("plain"),
                row("a\"b\\c\t\n\r"),
                row(""),

                row(LocalDateTime.of(2026, 1, 2, 3, 4, 5, 678_000_000)),
                row(LocalDate.of(2024, 6, 10)),

                Arrays.asList(1, "Zither", new BigDecimal("1250.5"), null, Boolean.TRUE));
    }

    /**
     * The conversions of decision 117, which both suites make before they render: the JSON of
     * LDBC's validation set and the rows of an artifact. They follow the rows above in the
     * conformance text, so the converters are held to one text the way the renderers are.
     */
    public static List<String> ldbcRows() {
        return LdbcConformance.rows();
    }

    /**
     * The canonical form of a whole result (decision 170), which both suites compute before
     * they compare and which follows the conversions in the conformance text: the rows of one
     * key value sorted by the rendered line while the sequence of key values stays; a key of
     * two fields; the whole row as the key, which keeps the rows as they came; no key, which
     * sorts the whole result.
     */
    public static List<String> canonicalRows() {
        List<List<Object>> byScore = List.of(
                Arrays.asList(60, "A"), Arrays.asList(53, "C"), Arrays.asList(53, "B"),
                Arrays.asList(7, "Z"), Arrays.asList(7, "Y"), Arrays.asList(7, "Y"));
        List<List<Object>> byTwo = List.of(
                Arrays.asList(1, "x", 3), Arrays.asList(1, "x", 2), Arrays.asList(1, "y", 1), Arrays.asList(2, "x", 0));
        List<List<Object>> pair = List.of(Arrays.asList(2, "b"), Arrays.asList(1, "a"));

        List<String> rows = new ArrayList<>();
        rows.addAll(ResultRow.canonical(byScore, settings(), List.of("Score", "Name"), List.of("Score")));
        rows.addAll(ResultRow.canonical(byTwo, settings(), List.of("A", "B", "C"), List.of("A", "B")));
        rows.addAll(ResultRow.canonical(pair, settings(), List.of("A", "B"), List.of("A", "B")));
        rows.addAll(ResultRow.canonical(pair, settings(), List.of("A", "B"), List.of()));
        return rows;
    }

    private static List<Object> row(Object value) {
        return Arrays.asList(value);
    }
}
