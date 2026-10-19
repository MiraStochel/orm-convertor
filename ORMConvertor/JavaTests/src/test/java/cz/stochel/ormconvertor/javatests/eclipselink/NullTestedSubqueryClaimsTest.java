package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.eclipse.persistence.sessions.Session;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The JPQL the readers of a LINQ source hand the JPA targets since decisions 155 and 156,
 * against EclipseLink 5.0.0: the Sum of EF Core read as {@code coalesce} over a scalar
 * subquery and over an aggregate of the group, and a scalar subquery tested for NULL - the
 * place the provider's compensation for NULL puts a subquery. Each is parsed and run over
 * three orders: order 1 has the quantities 5 and NULL, order 2 only a NULL, order 3 no line.
 */
@Tag("integration")
class NullTestedSubqueryClaimsTest {

    @Entity(name = "NsOrder")
    @Table(name = "NsOrders")
    public static class NsOrder {
        @Id
        @Column(name = "id")
        private Integer id;
    }

    @Entity(name = "NsLine")
    @Table(name = "NsLines")
    public static class NsLine {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "orderId")
        private Integer orderId;

        @Column(name = "quantity")
        private Integer quantity;
    }

    /** The hand-written entities name no schema, so the session qualifies their tables with the suite's. */
    public static final class SuiteSchema implements SessionCustomizer {
        @Override
        public void customize(Session session) {
            session.getLogin().setTableQualifier(TestDatabase.schemaName());
        }
    }

    private static EntityManagerFactory factory;

    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".NsOrders (id int PRIMARY KEY)");
            statement.execute("CREATE TABLE " + schema + ".NsLines (id int PRIMARY KEY, orderId int NOT NULL, quantity int NULL)");
            statement.execute("INSERT INTO " + schema + ".NsOrders VALUES (1), (2), (3)");
            statement.execute("INSERT INTO " + schema + ".NsLines VALUES (1, 1, 5), (2, 1, NULL), (3, 2, NULL)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                NullTestedSubqueryClaimsTest.class.getClassLoader(),
                null,
                List.of(NsOrder.class, NsLine.class),
                Map.of("eclipselink.session.customizer", SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** COALESCE over a scalar subquery: the orders whose lines sum to nothing - none at all, or only NULLs. */
    @Test
    void coalesceOverAScalarSubqueryIsRead() {
        assertEquals(List.of("2", "3"),
                rows("select o.id from NsOrder o where coalesce((select sum(l.quantity) from NsLine l where l.orderId = o.id), 0) = 0 order by o.id"));
    }

    /** COALESCE over an aggregate of the group, in the select list and in HAVING. */
    @Test
    void coalesceOverAnAggregateOfTheGroupIsRead() {
        assertEquals(List.of("1,5", "2,0"),
                rows("select l.orderId, coalesce(sum(l.quantity), 0) from NsLine l group by l.orderId order by l.orderId"));
        assertEquals(List.of("2"),
                rows("select l.orderId from NsLine l group by l.orderId having coalesce(sum(l.quantity), 0) = 0"));
    }

    /** A scalar subquery tested for NULL and for a value. */
    @Test
    void aScalarSubqueryIsTestedForNull() {
        assertEquals(List.of("2", "3"),
                rows("select o.id from NsOrder o where (select max(l.quantity) from NsLine l where l.orderId = o.id) is null order by o.id"));
        assertEquals(List.of("1"),
                rows("select o.id from NsOrder o where (select max(l.quantity) from NsLine l where l.orderId = o.id) is not null order by o.id"));
    }

    private static List<String> rows(String jpql) {
        EntityManager manager = factory.createEntityManager();
        try {
            List<String> rows = new ArrayList<>();
            for (Object row : manager.createQuery(jpql).getResultList()) {
                rows.add(row instanceof Object[] values
                        ? values[0] + "," + values[1]
                        : String.valueOf(row));
            }
            return rows;
        } finally {
            manager.close();
        }
    }
}
