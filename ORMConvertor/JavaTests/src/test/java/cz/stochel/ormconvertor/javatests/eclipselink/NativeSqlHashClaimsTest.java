package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
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
import org.eclipse.persistence.config.QueryHints;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 reads {@code #} in the text of a native query - the facts decision
 * 211 rests on, found by the runs of the SEDE catalog, whose texts number their rows under
 * the alias {@code [#]}. It reads {@code #} as the mark of a named parameter inside a
 * bracketed identifier wherever it stands there, and the query fails when it runs
 * ("The index 1 is out of range"); quotes keep it out only some of the time. Under the hint
 * {@code eclipselink.jdbc.parameter-delimiter} naming a character the text does not hold -
 * the builder's form, a control character - every such text runs and the positional
 * parameters still bind. Nothing here reads a table.
 */
@Tag("integration")
class NativeSqlHashClaimsTest {

    @Entity(name = "NhAnchor")
    @Table(name = "NhAnchors")
    public static class NhAnchor {
        @Id
        private Integer id;
    }

    /** The mark the builder names first: U+0001, which no text of the catalogs holds. */
    private static final String MARK = "\u0001";

    private static EntityManagerFactory factory;

    @BeforeAll
    static void open() {
        factory = EclipseLinkBootstrap.build("none", NhAnchor.class);
    }

    @AfterAll
    static void close() {
        factory.close();
    }

    @Test
    void aHashInABracketedIdentifierIsReadAsAParameter() {
        for (String sql : List.of("SELECT 1 AS [#]", "SELECT 1 AS [#x]", "SELECT 1 AS [a#b]", "SELECT 1 AS [#], 2 AS b")) {
            RuntimeException refused = assertThrows(RuntimeException.class, () -> rows(sql, false), sql);
            assertTrue(innermost(refused).contains("The index 1 is out of range"), sql + ": " + innermost(refused));
        }
    }

    /** Quotes are no way out: a quoted # alone runs, beside another quoted # and a parameter it is read again. */
    @Test
    void quotesKeepTheHashOutOnlySometimes() {
        assertEquals(1, single("SELECT 1 AS \"#\"", false));
        assertEquals("a #b c", single("SELECT 'a #b c' AS x", false));
        assertEquals(1, single("SELECT 1 AS \"a#b\" WHERE 1 = ?1", false, 1));

        RuntimeException refused = assertThrows(RuntimeException.class,
                () -> rows("SELECT 1 AS \"a#b\" WHERE 'x#' = ?1", false, "x#"));
        assertTrue(innermost(refused).contains("The index 1 is out of range"), innermost(refused));
    }

    /** The builder's form: under the hint nothing is a named parameter, and the positions bind as before. */
    @Test
    void underTheHintEveryHashIsText() {
        assertEquals(1, single("SELECT 1 AS [#]", true));
        assertEquals(1, single("SELECT 1 AS [a#b] WHERE 'x#' = ?1", true, "x#"));
        assertEquals(1, single("SELECT 1 AS \"a#b\" WHERE 'x#' = ?1", true, "x#"));
        assertEquals(1, single("SELECT 1 AS [#] WHERE 2 IN (?1, ?2) AND N'#x' = ?3", true, 1, 2, "#x"));
        assertArrayEquals(new Object[] {5, "z"}, (Object[]) single("SELECT ?1 AS [#], ?2 AS y", true, 5, "z"));
        assertArrayEquals(new Object[] {1, 2, 3, 4}, (Object[]) single("SELECT 1 AS [#], 2 AS [#a], 3 AS [b#], ?1 AS [##]", true, 4));
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
