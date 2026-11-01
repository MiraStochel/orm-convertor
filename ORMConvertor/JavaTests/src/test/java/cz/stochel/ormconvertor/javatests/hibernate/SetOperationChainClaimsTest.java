package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.List;
import java.util.TreeSet;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What Hibernate 7.4.5 does with a chain of two set operations - the spelling the JPQL
 * builder gives one (measured 2026-11-01): a bare chain is read left to right whatever its
 * operators - the SQL it writes wraps {@code a union b} in a derived table before the
 * intersect, against SQL Server's own precedence -, and a parenthesized operand is read on
 * either side, the right one grouping the right side. The builder therefore writes a chain
 * bare from the left and parenthesizes a nested right operand, which Hibernate takes. The
 * other half of the measurement is the EclipseLink test of the same name, whose parser takes
 * no parenthesized operand.
 *
 * <p>The sets: a = {1, 2, 3}, b = {2}, c = {2, 3} over the rows 1 to 4, so that the two
 * groupings of every mixed chain differ: (a union b) intersect c is {2, 3} where
 * a union (b intersect c) is {1, 2, 3}, and (a except b) except c is {1} where
 * a except (b except c) is {1, 2, 3}.
 */
@Tag("integration")
class SetOperationChainClaimsTest {

    @Entity(name = "ScValue")
    @Table(name = "ScValues")
    public static class ScValue {
        @Id
        @Column(name = "id")
        private Integer id;
    }

    static final String A = "select v.id from ScValue v where v.id <= 3";
    static final String B = "select v.id from ScValue v where v.id = 2";
    static final String C = "select v.id from ScValue v where v.id >= 2 and v.id <= 3";

    private static SessionFactory factory;
    private static Session session;

    @BeforeAll
    static void createTheTable() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".ScValues (id int PRIMARY KEY)");
            statement.execute("INSERT INTO " + schema + ".ScValues VALUES (1), (2), (3), (4)");
        }

        factory = HibernateBootstrap.build("none", ScValue.class);
        session = factory.openSession();
    }

    @AfterAll
    static void dropTheTable() throws Exception {
        if (session != null) {
            session.close();
        }
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** A bare chain is read, and left to right: the union's result is what the intersect and the except apply to. */
    @Test
    void aBareChainIsReadLeftToRight() {
        assertEquals(List.of(1, 2, 3), rows(A + " union " + B + " union " + C));
        assertEquals(List.of(1, 2, 3), rows(A + " union all " + B + " union all " + C));
        assertEquals(List.of(2, 3), rows(A + " union " + B + " intersect " + C));
        assertEquals(List.of(2, 3), rows(A + " intersect " + B + " union " + C));
        assertEquals(List.of(1), rows(A + " except " + B + " except " + C));
    }

    /** The left operand in parentheses means the same as the bare chain. */
    @Test
    void aParenthesizedLeftOperandIsRead() {
        assertEquals(List.of(1, 2, 3), rows("(" + A + " union " + B + ") union " + C));
        assertEquals(List.of(2, 3), rows("(" + A + " union " + B + ") intersect " + C));
    }

    /** The right operand in parentheses groups the right side - the spelling the builder keeps for a nested right operand. */
    @Test
    void aParenthesizedRightOperandGroupsTheRightSide() {
        assertEquals(List.of(1, 2, 3), rows(A + " union (" + B + " union " + C + ")"));
        assertEquals(List.of(1, 2, 3), rows(A + " union (" + B + " intersect " + C + ")"));
        assertEquals(List.of(1, 2, 3), rows(A + " except (" + B + " except " + C + ")"));
    }

    private static List<Integer> rows(String hql) {
        return List.copyOf(new TreeSet<>(session.createSelectionQuery(hql, Integer.class).getResultList()));
    }
}
