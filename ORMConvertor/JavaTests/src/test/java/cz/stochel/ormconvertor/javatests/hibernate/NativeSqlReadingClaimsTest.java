package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Query;
import jakarta.persistence.Table;
import java.util.List;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What Hibernate 7.4.5 reads as its own in the text of a native query - the facts decision
 * 217 rests on. Its reading does not know the square brackets of T-SQL, so inside a
 * bracketed identifier a {@code ?} is a parameter, {@code :name} a named one, a backslash
 * is swallowed with the next character passed as text, and an apostrophe or a quote opens
 * a literal in which a later parameter is not found. A backslash before each of the five
 * passes it as text - the builder's form -, beside bound parameters too, and the name
 * reaches the database as the source wrote it. Quotes, a literal and a comment keep all of
 * it out. Nothing here reads a table.
 */
@Tag("integration")
class NativeSqlReadingClaimsTest {

    @Entity(name = "NrAnchor")
    @Table(name = "NrAnchors")
    public static class NrAnchor {
        @Id
        private Integer id;
    }

    private static SessionFactory factory;

    @BeforeAll
    static void open() {
        factory = HibernateBootstrap.build("none", NrAnchor.class);
    }

    @AfterAll
    static void close() {
        factory.close();
    }

    @Test
    void aBracketedIdentifierIsReadLikeTheRestOfTheText() {
        assertRefused("No argument for ordinal parameter", "SELECT 1 AS [a?b]");
        assertRefused("No argument for ordinal parameter", "SELECT 1 AS [?]");
        assertRefused("Cannot mix parameter styles", "SELECT 1 AS [a?b] WHERE 1 = ?1", 1);
        assertRefused("No argument for named parameter", "SELECT 1 AS [a:b]");
        assertRefused("No parameter labelled", "SELECT 1 AS [it's] WHERE 1 = ?1", 1);
        assertRefused("No parameter labelled", "SELECT 1 AS [a\"b] WHERE 1 = ?1", 1);

        // No refusal, another name: the backslash is gone from the column the consumer reads.
        assertEquals("[{\"cd\":1}]", json("SELECT 1 AS [c\\d] FOR JSON PATH"));
    }

    @Test
    void aColonThatStartsNoNameAndABraceAreText() {
        assertEquals("[{\"a: b\":1}]", json("SELECT 1 AS [a: b] FOR JSON PATH"));
        assertEquals("[{\"a::b\":1}]", json("SELECT 1 AS [a::b] FOR JSON PATH"));
        assertEquals("[{\"a{b}c\":1}]", json("SELECT 1 AS [a{b}c] FOR JSON PATH"));
    }

    /** The builder's form: every one of the five behind a backslash, whatever follows it. */
    @Test
    void aBackslashPassesEachOfThemAsText() {
        assertEquals(
                "[{\"a?b\":1,\"c:d\":2,\"e\\\\f\":3,\"g'h\":4,\"i]?j\":5,\"k\\\"l\":6}]",
                json(
                        "SELECT 1 AS [a\\?b], 2 AS [c\\:d], 3 AS [e\\\\f], 4 AS [g\\'h], 5 AS [i]]\\?j], 6 AS [k\\\"l]"
                                + " WHERE 2 IN (?1, ?2) AND 'x:y?z\\w' = ?3 FOR JSON PATH",
                        1, 2, "x:y?z\\w"));
        assertEquals("[{\"m: n\":1,\"o::p\":2,\"?\":3}]", json("SELECT 1 AS [m\\: n], 2 AS [o\\:\\:p], 3 AS [\\?] FOR JSON PATH"));
    }

    @Test
    void quotesALiteralAndACommentKeepThemOut() {
        assertEquals("[{\"a?b\":1}]", json("SELECT 1 AS \"a?b\" WHERE 1 = ?1 FOR JSON PATH", 1));
        assertEquals("[{\"x\":\"a?b\"}]", json("SELECT N'a?b' AS x WHERE 1 = ?1 FOR JSON PATH", 1));
        assertEquals("[{\"x\":\"a\\\\b:c\"}]", json("SELECT 'a\\b:c' AS x FOR JSON PATH"));
        assertEquals(1, single("SELECT 1 AS x -- a?b\n WHERE 1 = ?1", 1));
        assertEquals(1, single("SELECT /* a?b :c */ 1 AS x WHERE 1 = ?1", 1));
        assertEquals(1, single("SELECT 1 AS x /* a#b */"));
    }

    private static void assertRefused(String message, String sql, Object... parameters) {
        RuntimeException refused = assertThrows(RuntimeException.class, () -> single(sql, parameters), sql);
        assertTrue(innermost(refused).contains(message), sql + ": " + innermost(refused));
    }

    /** The one row of a FOR JSON query: the names of the columns as the database read them. */
    private static String json(String sql, Object... parameters) {
        return String.valueOf(single(sql, parameters));
    }

    private static Object single(String sql, Object... parameters) {
        // The untyped call of Jakarta Persistence, the one the builder's method makes; a session
        // of its own, so that a refused text leaves nothing behind for the next one.
        try (Session session = factory.openSession()) {
            Query query = session.createNativeQuery(sql);
            for (int i = 0; i < parameters.length; i++) {
                query.setParameter(i + 1, parameters[i]);
            }
            List<?> rows = query.getResultList();
            assertEquals(1, rows.size(), sql);
            return rows.get(0);
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
