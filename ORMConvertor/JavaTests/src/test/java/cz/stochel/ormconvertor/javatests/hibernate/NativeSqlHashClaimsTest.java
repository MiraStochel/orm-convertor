package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;

import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.util.List;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.hibernate.query.NativeQuery;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The other side of decision 211: Hibernate 7.4.5 reads no {@code #} in the text of a
 * native query, bracketed, quoted or beside a positional parameter, so its profile names no
 * hint and the builder writes the text as it stands. Nothing here reads a table.
 */
@Tag("integration")
class NativeSqlHashClaimsTest {

    @Entity(name = "NhAnchor")
    @Table(name = "NhAnchors")
    public static class NhAnchor {
        @Id
        private Integer id;
    }

    private static SessionFactory factory;
    private static Session session;

    @BeforeAll
    static void open() {
        factory = HibernateBootstrap.build("none", NhAnchor.class);
        session = factory.openSession();
    }

    @AfterAll
    static void close() {
        session.close();
        factory.close();
    }

    @Test
    void aHashIsText() {
        assertEquals(1, single("SELECT 1 AS [#]"));
        assertEquals(1, single("SELECT 1 AS [a#b]"));
        assertEquals(1, single("SELECT 1 AS [a#b] WHERE 'x#' = ?1", "x#"));
        assertEquals(1, single("SELECT 1 AS \"a#b\" WHERE 'x#' = ?1", "x#"));
        assertArrayEquals(new Object[] {5, "z"}, (Object[]) single("SELECT ?1 AS [#], ?2 AS y", 5, "z"));
    }

    private static Object single(String sql, Object... parameters) {
        // The untyped call of Jakarta Persistence, the one the builder's method makes.
        NativeQuery<?> query = (NativeQuery<?>) session.createNativeQuery(sql);
        for (int i = 0; i < parameters.length; i++) {
            query.setParameter(i + 1, parameters[i]);
        }
        List<?> rows = query.getResultList();
        assertEquals(1, rows.size(), sql);
        return rows.get(0);
    }
}
