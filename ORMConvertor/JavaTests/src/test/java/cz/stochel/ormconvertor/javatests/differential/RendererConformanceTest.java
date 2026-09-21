package cz.stochel.ormconvertor.javatests.differential;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import org.junit.jupiter.api.Test;

/**
 * That both ecosystems render a row into the same text (decision 089). It is the first
 * thing the differential verification needs, because every comparison it makes rests on
 * it: BigDecimal and decimal print trailing zeros differently, LocalDateTime and DateTime
 * format midnight differently, and a tab inside a string breaks the separator if one side
 * forgets to escape it.
 *
 * <p>Neither suite records the expected text. It is checked in, both render against it,
 * and a disagreement therefore shows up as a failure on the side that drifted rather than
 * as two suites quietly agreeing on something new.
 */
class RendererConformanceTest {

    @Test
    void renderedRowsMatchTheConformanceText() {
        List<String> expected = DifferentialData.readLines("renderer-conformance.txt");

        List<String> actual = new ArrayList<>();
        for (List<Object> row : RendererConformance.rows()) {
            actual.add(ResultRow.render(row, RendererConformance.settings()));
        }

        assertEquals(expected.size(), actual.size(), "the conformance text and the rendered rows differ in count");
        assertEquals(expected, actual);
    }

    /**
     * A type nobody wrote a rule for has to stop the run. The canonical form is only worth
     * something while every value in it got there by a stated rule, so the renderer guesses
     * at nothing - not even at something as obvious as a UUID.
     */
    @Test
    void aValueWithNoRuleIsRefused() {
        UnsupportedOperationException refused = assertThrows(
                UnsupportedOperationException.class,
                () -> ResultRow.renderField(UUID.randomUUID(), RendererConformance.settings()));

        assertTrue(refused.getMessage().contains("089"), refused.getMessage());
    }
}
