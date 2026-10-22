package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestSchema;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * A claim of decision 161 and one of decision 162, measured against Hibernate 7.4.5 over SQL Server: an aggregate
 * over a window - {@code avg(x) over (partition by …)}, with and without an ordering, which
 * makes it a running aggregate - computes what the same T-SQL computes, and a subquery in the
 * {@code on} condition of an entity join - {@code in (select …)}, {@code not in}, a correlated
 * one, in an inner and in a left join - is accepted and joins the rows the same T-SQL joins.
 * The entities are the hand-written ones of {@link HibernateClaimsTest}; three customers and
 * one profile are inserted in a transaction rolled back at the end.
 */
@Tag("integration")
class WindowAggregateAndJoinSubqueryClaimsTest {

    @BeforeAll
    static void createSchema() throws Exception {
        TestSchema.create();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
    }

    // ---- claim 1: an aggregate over a window --------------------------------------------

    /** The average of a partition beside every row of it, as T-SQL computes {@code AVG(x) OVER (PARTITION BY …)}. */
    @Test
    void anAggregateOverAPartition() {
        assertEquals(List.of("Alpha,2", "Alpha,2", "Beta,1"), rows(s -> s.createSelectionQuery(
                "select c.name, count(c.customerId) over (partition by c.name) from Customer c order by c.name, c.customerId",
                Object[].class).getResultList()));
    }

    /** With an ordering the window is a running aggregate - the default frame of SQL Server, which Hibernate passes through. */
    @Test
    void anAggregateOverAnOrderedWindowRuns() {
        assertEquals(List.of("Alpha,1", "Alpha,2", "Beta,1"), rows(s -> s.createSelectionQuery(
                "select c.name, count(c.customerId) over (partition by c.name order by c.customerId) from Customer c order by c.name, c.customerId",
                Object[].class).getResultList()));
    }

    /** The other aggregates of the vocabulary stand over a window the same way. */
    @Test
    void theOtherAggregatesStandOverAWindow() {
        assertEquals(3, rows(s -> s.createSelectionQuery(
                "select c.customerId, max(c.customerId) over (partition by c.name), min(c.customerId) over (partition by c.name), sum(c.customerId) over (), avg(c.customerId) over (partition by c.name) from Customer c",
                Object[].class).getResultList()).size());
    }

    /** The window inside a WITH and the filter over it outside, the shape of FIBEN q237. */
    @Test
    void anAggregateOverAWindowInsideAnIntermediateResult() {
        assertEquals(List.of("Alpha,2", "Alpha,2"), rows(s -> s.createSelectionQuery(
                """
                with perName as (select c.customerId as id, c.name as name, count(c.customerId) over (partition by c.name) as n from Customer c)
                select p.name, p.n from perName p where p.n > 1 order by p.id
                """,
                Object[].class).getResultList()));
    }

    // ---- claim 2: a subquery in the on condition of an entity join -----------------------

    /** {@code in (select …)} in an inner join keeps the customer the subquery names. */
    @Test
    void inOverASubqueryInAnInnerJoin() {
        assertEquals(List.of("Alpha,https://alpha.example"), rows(s -> s.createSelectionQuery(
                "select c.name, p.website from Customer c join CustomerProfile p on p.customerId = c.customerId and c.name in (select c2.name from Customer c2 where c2.name = 'Alpha')",
                Object[].class).getResultList()));
    }

    /** {@code not in (select …)} drops it. */
    @Test
    void notInOverASubqueryInAnInnerJoin() {
        assertEquals(List.of(), rows(s -> s.createSelectionQuery(
                "select c.name, p.website from Customer c join CustomerProfile p on p.customerId = c.customerId and c.name not in (select c2.name from Customer c2 where c2.name = 'Alpha')",
                Object[].class).getResultList()));
    }

    /** A correlated subquery in a left join: every customer stays, only the one the subquery names gets a profile. */
    @Test
    void aCorrelatedSubqueryInALeftJoin() {
        assertEquals(List.of("Alpha,https://alpha.example", "Alpha,null", "Beta,null"), rows(s -> s.createSelectionQuery(
                "select c.name, p.website from Customer c left join CustomerProfile p on p.customerId = c.customerId and c.customerId in (select c2.customerId from Customer c2 where c2.customerId = c.customerId and c2.name = 'Alpha') order by c.name, c.customerId",
                Object[].class).getResultList()));
    }

    /** {@code exists} in the on condition. */
    @Test
    void existsInAnInnerJoin() {
        assertEquals(List.of("Alpha,https://alpha.example"), rows(s -> s.createSelectionQuery(
                "select c.name, p.website from Customer c join CustomerProfile p on p.customerId = c.customerId and exists (select c2.customerId from Customer c2 where c2.customerId = c.customerId and c2.name = 'Alpha')",
                Object[].class).getResultList()));
    }

    /**
     * Three customers, the first with a profile, inserted in a transaction rolled back at the
     * end. Alpha and Alpha share a name so that a partition has two rows. Returns each row as
     * its values joined by commas.
     */
    private static List<String> rows(Function<Session, List<Object[]>> query) {
        try (SessionFactory factory = HibernateBootstrap.build("none", Customer.class, CustomerProfile.class);
             Session session = factory.openSession()) {
            session.beginTransaction();
            try {
                Customer alpha = new Customer("Alpha");
                session.persist(alpha);
                session.persist(new Customer("Alpha"));
                session.persist(new Customer("Beta"));
                session.flush();
                session.persist(new CustomerProfile(alpha.getCustomerId(), "https://alpha.example"));
                session.flush();

                List<String> rows = new ArrayList<>();
                for (Object[] row : query.apply(session)) {
                    StringBuilder line = new StringBuilder();
                    for (Object value : row) {
                        if (line.length() > 0) {
                            line.append(',');
                        }
                        line.append(value);
                    }
                    rows.add(line.toString());
                }
                return rows;
            } finally {
                session.getTransaction().rollback();
            }
        }
    }
}
