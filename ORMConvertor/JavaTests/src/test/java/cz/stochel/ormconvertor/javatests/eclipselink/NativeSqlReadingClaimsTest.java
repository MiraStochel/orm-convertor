package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.Query;
import jakarta.persistence.Table;
import java.util.List;
import org.eclipse.persistence.config.HintValues;
import org.eclipse.persistence.config.QueryHints;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What EclipseLink 5.0.0 reads as its own in the text of a native query beside the
 * {@code #} of decision 211 - the facts decision 217 rests on. A {@code ?} is a positional
 * parameter wherever it stands outside a string literal: inside a bracketed identifier,
 * inside a quoted one and inside a comment, with the hint of decision 211 and without it,
 * and the query fails when it runs ("The index 1 is out of range"). Nothing escapes it, so
 * the builder refuses a native query whose bracketed name holds one. A colon, a backslash,
 * an apostrophe and a quote inside a bracketed identifier are text. The shared writer
 * writes no comment and no quoted identifier; those two are measured so that the limit is
 * known, not because the builder meets them. Nothing here reads a table.
 */
@Tag("integration")
class NativeSqlReadingClaimsTest {

    @Entity(name = "NrAnchor")
    @Table(name = "NrAnchors")
    public static class NrAnchor {
        @Id
        private Integer id;
    }

    /** The mark the builder names first under decision 211. */
    private static final String MARK = "\u0001";

    private static final String REFUSED = "The index 1 is out of range";

    private static EntityManagerFactory factory;

    @BeforeAll
    static void open() {
        factory = EclipseLinkBootstrap.build("none", NrAnchor.class);
    }

    @AfterAll
    static void close() {
        factory.close();
    }

    @Test
    void aQuestionMarkInAnIdentifierIsReadAsAParameterWithTheHintAndWithoutIt() {
        for (boolean hinted : new boolean[] {false, true}) {
            for (String sql : List.of("SELECT 1 AS [a?b]", "SELECT 1 AS [?]", "SELECT 1 AS \"a?b\"", "SELECT 1 AS \"?\"")) {
                assertRefused(REFUSED, sql, hinted);
            }

            assertRefused(REFUSED, "SELECT 1 AS [a?b] WHERE 1 = ?1", hinted, 1);
            assertRefused(REFUSED, "SELECT 1 AS \"a?b\" WHERE 1 = ?1", hinted, 1);
        }
    }

    /** Neither a doubled mark nor a backslash keeps it text, and with the binding off NULL is written into the name. */
    @Test
    void nothingEscapesAQuestionMarkInAnIdentifier() {
        for (boolean hinted : new boolean[] {false, true}) {
            assertRefused(REFUSED, "SELECT 1 AS [a??b]", hinted);
            assertRefused(REFUSED, "SELECT 1 AS [a\\?b]", hinted);
        }

        EntityManager manager = factory.createEntityManager();
        try {
            Query inlined = manager.createNativeQuery("SELECT 1 AS [a?b]").setHint(QueryHints.BIND_PARAMETERS, HintValues.FALSE);
            RuntimeException refused = assertThrows(RuntimeException.class, inlined::getResultList);
            assertTrue(innermost(refused).contains("aNULL"), innermost(refused));
        } finally {
            manager.close();
        }
    }

    /** A string literal as the alias runs, but only where nothing names the column again. */
    @Test
    void aLiteralAsTheAliasRunsAndAReferenceToItDoesNot() {
        assertEquals("[{\"a?b\":1}]", json("SELECT 1 AS 'a?b' WHERE 1 = ?1 FOR JSON PATH", false, 1));
        assertRefused(REFUSED, "SELECT t.[a?b] FROM (SELECT 1 AS 'a?b') t", false);
    }

    @Test
    void theOtherCharactersOfABracketedIdentifierAreText() {
        for (boolean hinted : new boolean[] {false, true}) {
            assertEquals("[{\"a:b\":1}]", json("SELECT 1 AS [a:b] FOR JSON PATH", hinted));
            assertEquals("[{\"c\\\\d\":1}]", json("SELECT 1 AS [c\\d] FOR JSON PATH", hinted));
            assertEquals("[{\"it's\":1}]", json("SELECT 1 AS [it's] WHERE 1 = ?1 FOR JSON PATH", hinted, 1));
            assertEquals("[{\"a\\\"b\":1}]", json("SELECT 1 AS [a\"b] WHERE 1 = ?1 FOR JSON PATH", hinted, 1));
            assertEquals("[{\"a{b}c\":1}]", json("SELECT 1 AS [a{b}c] FOR JSON PATH", hinted));
        }
    }

    @Test
    void aQuestionMarkInALiteralIsText() {
        for (boolean hinted : new boolean[] {false, true}) {
            assertEquals("a?b", single("SELECT 'a?b' AS x", hinted));
            assertEquals("?", single("SELECT '?' AS x", hinted));
            assertEquals("a?b", single("SELECT N'a?b' AS x WHERE 1 = ?1", hinted, 1));
            assertEquals(1, single("SELECT 1 AS x WHERE 'a?b' = ?1", hinted, "a?b"));
            assertEquals("it's ?", single("SELECT 'it''s ?' AS x WHERE 1 = ?1", hinted, 1));
        }
    }

    /** A comment is read like the rest of the text: its ? takes a position, before the numbered ones. */
    @Test
    void aQuestionMarkInACommentIsReadAsAParameter() {
        for (boolean hinted : new boolean[] {false, true}) {
            assertRefused(REFUSED, "SELECT 1 AS x -- a?b", hinted);
            assertRefused(REFUSED, "SELECT 1 AS x -- a?b\n", hinted);
            assertRefused(REFUSED, "SELECT 1 AS x /* a?b */", hinted);
            assertRefused("The index 2 is out of range", "SELECT /* a?b */ 1 AS x WHERE 1 = ?1", hinted, 1);
            assertRefused("The index 2 is out of range", "SELECT 1 AS x -- a?b\n WHERE 1 = ?1", hinted, 1);
        }
    }

    /** The # of decision 211 is read in a comment too, and the hint of that decision reaches it there. */
    @Test
    void aHashInACommentIsReadUntilTheHintNamesAnotherMark() {
        assertRefused(REFUSED, "SELECT 1 AS x /* a#b */", false);
        assertEquals(1, single("SELECT 1 AS x /* a#b */", true));
        assertEquals(1, single("SELECT 1 AS x -- a#b\n WHERE 1 = ?1", true, 1));
    }

    private static void assertRefused(String message, String sql, boolean hinted, Object... parameters) {
        RuntimeException refused = assertThrows(RuntimeException.class, () -> rows(sql, hinted, parameters), sql);
        assertTrue(innermost(refused).contains(message), sql + ": " + innermost(refused));
    }

    /** The one row of a FOR JSON query: the names of the columns as the database read them. */
    private static String json(String sql, boolean hinted, Object... parameters) {
        return String.valueOf(single(sql, hinted, parameters));
    }

    private static Object single(String sql, boolean hinted, Object... parameters) {
        List<?> rows = rows(sql, hinted, parameters);
        assertEquals(1, rows.size(), sql);
        return rows.get(0);
    }

    private static List<?> rows(String sql, boolean hinted, Object... parameters) {
        EntityManager manager = factory.createEntityManager();
        try {
            Query query = manager.createNativeQuery(sql);
            if (hinted) {
                query.setHint(QueryHints.PARAMETER_DELIMITER, MARK);
            }
            for (int i = 0; i < parameters.length; i++) {
                query.setParameter(i + 1, parameters[i]);
            }
            return query.getResultList();
        } finally {
            manager.close();
        }
    }

    private static String innermost(Throwable thrown) {
        Throwable root = thrown;
        while (root.getCause() != null) {
            root = root.getCause();
        }
        return String.valueOf(root.getMessage());
    }
}
