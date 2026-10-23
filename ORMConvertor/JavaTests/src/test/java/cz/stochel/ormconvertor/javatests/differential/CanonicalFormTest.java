package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNotEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.Arrays;
import java.util.List;
import org.junit.jupiter.api.Test;

/**
 * The canonical form of a whole result (decision 170), the case the differential verification
 * documents about its comparison - the counterpart of {@code CanonicalFormTest.cs}: the order
 * inside a run of rows that agree on the ordering key is not compared, the order of the key
 * values is, and a query without an ordering is a set.
 */
class CanonicalFormTest {

    private static final ResultRow.Settings SETTINGS = ResultRow.Settings.defaults();
    private static final List<String> FIELDS = List.of("Score", "Name");

    /** Rows of one key value are the same result in whichever order they came. */
    @Test
    void rowsOfOneKeyValueCompareAsASet() {
        List<String> first = ResultRow.canonical(rows(row(60, "A"), row(53, "C"), row(53, "B")), SETTINGS, FIELDS, List.of("Score"));
        List<String> second = ResultRow.canonical(rows(row(60, "A"), row(53, "B"), row(53, "C")), SETTINGS, FIELDS, List.of("Score"));

        assertEquals(first, second);
        assertEquals(List.of("60\t\"A\"", "53\t\"B\"", "53\t\"C\""), first);
    }

    /** The sequence of key values is what the query determines, so another sequence is another result. */
    @Test
    void anotherOrderOfTheKeyValuesIsAnotherResult() {
        List<String> ordered = ResultRow.canonical(rows(row(60, "A"), row(53, "B")), SETTINGS, FIELDS, List.of("Score"));
        List<String> reversed = ResultRow.canonical(rows(row(53, "B"), row(60, "A")), SETTINGS, FIELDS, List.of("Score"));

        assertNotEquals(ordered, reversed);
    }

    /** A key the result does not show is the whole row, and the rows are compared as they came. */
    @Test
    void theWholeRowAsTheKeyKeepsTheRowsAsTheyCame() {
        assertEquals(List.of("53\t\"B\"", "60\t\"A\""),
                ResultRow.canonical(rows(row(53, "B"), row(60, "A")), SETTINGS, FIELDS, FIELDS));
    }

    /** Without an ordering the whole result is a set. */
    @Test
    void withoutAKeyTheWholeResultIsASet() {
        assertEquals(List.of("53\t\"B\"", "60\t\"A\""),
                ResultRow.canonical(rows(row(60, "A"), row(53, "B")), SETTINGS, FIELDS, List.of()));
    }

    /** A key that names a field the row has not stops the run rather than comparing by a guess. */
    @Test
    void aKeyOutsideTheRowIsRefused() {
        IllegalStateException refused = assertThrows(
                IllegalStateException.class,
                () -> ResultRow.canonical(rows(row(60, "A")), SETTINGS, FIELDS, List.of("Rank")));

        assertTrue(refused.getMessage().contains("170"), refused.getMessage());
    }

    private static List<Object> row(Object... fields) {
        return Arrays.asList(fields);
    }

    @SafeVarargs
    private static List<List<Object>> rows(List<Object>... rows) {
        return List.of(rows);
    }
}
