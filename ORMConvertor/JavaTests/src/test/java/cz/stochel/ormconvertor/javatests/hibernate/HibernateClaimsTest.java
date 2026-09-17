package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.ResultSet;
import java.util.List;
import java.util.function.Function;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;

/**
 * The two claims of decision 077 that xUnit cannot verify and the Java suite was to
 * confirm first: how {@code @Column} precision on a {@code LocalDateTime} reaches the
 * generated column in Hibernate 7.4.5, and that Hibernate accepts the entity join with
 * {@code on} in every position the JPQL builder emits it. The entities are written by
 * hand in the shape the builder writes them; the generated artifacts themselves reach
 * this suite over HTTP from a running instance (decision 078), once that work is done.
 */
class HibernateClaimsTest {

    @BeforeAll
    static void createSchema() throws Exception {
        TestSchema.create();
    }

    @AfterAll
    static void dropSchema() throws Exception {
        TestSchema.drop();
    }

    // ---- claim 1: precision of a LocalDateTime column -----------------------------------

    /**
     * Decision 077 expected {@code @Column(precision = 3)} to yield {@code datetime2(3)}.
     * The first run of this suite (2026-09-17) showed otherwise: Hibernate 7.4.5 ignores
     * {@code precision} on a temporal column and emits the dialect's default,
     * {@code datetime2(7)} on SQL Server; the attribute that does reach the column is
     * {@code secondPrecision}, which Jakarta Persistence 3.2 added for exactly this. The
     * test states the measured behaviour of the pinned release - the consequence for the
     * builder is an open item.
     */
    @Test
    void precisionIsIgnoredOnLocalDateTimeWhereSecondPrecisionIsNot() throws Exception {
        // Schema generation runs while the factory is built; the table lands in the
        // suite's schema, and the schema's catalog-driven drop takes it away again.
        try (SessionFactory ignored = HibernateBootstrap.build("create", Stamp.class)) {
            // nothing to run - the DDL is the point
        }

        assertEquals(List.of("datetime2", "7"), columnType("Stamps", "PlacedAt"), "@Column(precision = 3)");
        assertEquals(List.of("datetime2", "3"), columnType("Stamps", "SeenAt"), "@Column(secondPrecision = 3)");
    }

    private static List<String> columnType(String table, String column) throws Exception {
        try (Connection connection = TestDatabase.open();
             PreparedStatement statement = connection.prepareStatement("""
                     SELECT DATA_TYPE, DATETIME_PRECISION
                     FROM INFORMATION_SCHEMA.COLUMNS
                     WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ? AND COLUMN_NAME = ?
                     """)) {
            statement.setString(1, TestDatabase.schemaName());
            statement.setString(2, table);
            statement.setString(3, column);
            try (ResultSet row = statement.executeQuery()) {
                assertTrue(row.next(), "the generated table " + table + " has no column " + column);
                return List.of(row.getString("DATA_TYPE"), row.getString("DATETIME_PRECISION"));
            }
        }
    }

    // ---- claim 2: the entity join with on, in every position the builder emits it -------

    /** The builder's shape: {@code <kind> Entity alias on <condition>}, right after the source. */
    @Test
    void innerEntityJoinInTheMainQuery() {
        assertEquals(1, rows(s -> s.createSelectionQuery(
                "select c from Customer c join CustomerProfile p on p.customerId = c.customerId",
                Customer.class).getResultList()));
    }

    @Test
    void leftEntityJoinInTheMainQuery() {
        assertEquals(2, rows(s -> s.createSelectionQuery(
                "select c from Customer c left join CustomerProfile p on p.customerId = c.customerId",
                Customer.class).getResultList()));
    }

    @Test
    void rightEntityJoinInTheMainQuery() {
        assertEquals(1, rows(s -> s.createSelectionQuery(
                "select c.customerId, p.customerId from Customer c right join CustomerProfile p on p.customerId = c.customerId",
                Object[].class).getResultList()));
    }

    /** {@code JoinKind.Full} is expressible per the Hibernate descriptor; this is what backs that. */
    @Test
    void fullEntityJoinInTheMainQuery() {
        assertEquals(2, rows(s -> s.createSelectionQuery(
                "select c.customerId, p.customerId from Customer c full join CustomerProfile p on p.customerId = c.customerId",
                Object[].class).getResultList()));
    }

    /** The builder renders a sub-query with the same clauses, so a join may sit inside exists (...). */
    @Test
    void entityJoinInsideAnExistsSubQuery() {
        assertEquals(1, rows(s -> s.createSelectionQuery(
                """
                select c from Customer c
                where exists (select p.customerId from CustomerProfile p join Customer c2 on c2.customerId = p.customerId
                              where c2.customerId = c.customerId)
                """,
                Customer.class).getResultList()));
    }

    /** ... and inside in (...). */
    @Test
    void entityJoinInsideAnInSubQuery() {
        assertEquals(1, rows(s -> s.createSelectionQuery(
                """
                select c from Customer c
                where c.customerId in (select p.customerId from CustomerProfile p join Customer c2 on c2.customerId = p.customerId)
                """,
                Customer.class).getResultList()));
    }

    /** Set operations are composed from operands, each of which may carry a join. */
    @Test
    void entityJoinInsideASetOperationOperand() {
        assertEquals(3, rows(s -> s.createSelectionQuery(
                """
                select c.name from Customer c join CustomerProfile p on p.customerId = c.customerId
                union all
                select c.name from Customer c
                """,
                String.class).getResultList()));
    }

    /**
     * Two customers, one of them with a profile, inserted in a transaction that is
     * rolled back at the end - the fixture's rule for a writing test. Returns how many
     * rows the query saw.
     */
    private static int rows(Function<Session, List<?>> query) {
        try (SessionFactory factory = HibernateBootstrap.build("none", Customer.class, CustomerProfile.class);
             Session session = factory.openSession()) {
            session.beginTransaction();
            try {
                Customer alpha = new Customer("Alpha");
                session.persist(alpha);
                session.persist(new Customer("Beta"));
                session.flush();
                session.persist(new CustomerProfile(alpha.getCustomerId(), "https://alpha.example"));
                session.flush();

                return query.apply(session).size();
            } finally {
                session.getTransaction().rollback();
            }
        }
    }
}
