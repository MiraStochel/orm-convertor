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
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * What EclipseLink 5.0.0 does with a subquery in the {@code on} condition of an entity join
 * (decision 162): {@code in (select …)}, {@code not in}, a correlated one, in an inner and in
 * a left join. Each query's rows are compared with what the same T-SQL returns, because the
 * {@code having} clause showed that this release can return other rows without an error
 * ({@link JoinedHavingClaimsTest}); a refusal at {@code createQuery} would be the other
 * measurable answer. The JPQL builder writes the shape for EclipseLink only where the
 * measurement says the rows are the same.
 */
@Tag("integration")
class JoinSubqueryClaimsTest {

    @Entity(name = "JsOrder")
    @Table(name = "JsOrders")
    public static class JsOrder {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "customerId")
        private Integer customerId;
    }

    @Entity(name = "JsLine")
    @Table(name = "JsLines")
    public static class JsLine {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "orderId")
        private Integer orderId;
    }

    private static EntityManagerFactory factory;

    /** Orders 1 and 2 of customer 100, order 3 of customer 200; lines 1 and 2 on order 1, line 3 on order 2, line 4 on order 3. */
    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".JsOrders (id int PRIMARY KEY, customerId int NOT NULL)");
            statement.execute("CREATE TABLE " + schema + ".JsLines (id int PRIMARY KEY, orderId int NOT NULL "
                    + "REFERENCES " + schema + ".JsOrders (id))");
            statement.execute("INSERT INTO " + schema + ".JsOrders VALUES (1, 100), (2, 100), (3, 200)");
            statement.execute("INSERT INTO " + schema + ".JsLines VALUES (1, 1), (2, 1), (3, 2), (4, 3)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                JoinSubqueryClaimsTest.class.getClassLoader(),
                null,
                List.of(JsOrder.class, JsLine.class),
                Map.of("eclipselink.session.customizer", JoinedHavingClaimsTest.SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** Customer 200 alone: order 3, line 4. */
    @Test
    void inOverASubqueryInAnInnerJoin() {
        assertEquals(List.of("3,4"), rows("select o.id, l.id from JsOrder o join JsLine l on l.orderId = o.id"
                + " and o.customerId in (select o2.customerId from JsOrder o2 where o2.id = 3) order by l.id"));
    }

    /** The complement: customer 100, orders 1 and 2, lines 1 to 3. */
    @Test
    void notInOverASubqueryInAnInnerJoin() {
        assertEquals(List.of("1,1", "1,2", "2,3"), rows("select o.id, l.id from JsOrder o join JsLine l on l.orderId = o.id"
                + " and o.customerId not in (select o2.customerId from JsOrder o2 where o2.id = 3) order by l.id"));
    }

    /** A left join keeps every order; only the one the subquery names gets its line. */
    @Test
    void inOverASubqueryInALeftJoin() {
        assertEquals(List.of("1,null", "2,null", "3,4"), rows("select o.id, l.id from JsOrder o left join JsLine l on l.orderId = o.id"
                + " and o.customerId in (select o2.customerId from JsOrder o2 where o2.id = 3) order by o.id, l.id"));
    }

    /** A correlated subquery: the orders of a customer after its first - orders 2 and 3. */
    @Test
    void aCorrelatedSubqueryInAnInnerJoin() {
        assertEquals(List.of("2,3", "3,4"), rows("select o.id, l.id from JsOrder o join JsLine l on l.orderId = o.id"
                + " and o.id in (select o2.id from JsOrder o2 where o2.customerId = o.customerId and o2.id > 1) order by l.id"));
    }

    /** {@code exists} in the condition. */
    @Test
    void existsInAnInnerJoin() {
        assertEquals(List.of("2,3", "3,4"), rows("select o.id, l.id from JsOrder o join JsLine l on l.orderId = o.id"
                + " and exists (select o2.id from JsOrder o2 where o2.customerId = o.customerId and o2.id > 1 and o2.id = o.id) order by l.id"));
    }

    private static List<String> rows(String jpql) {
        EntityManager manager = factory.createEntityManager();
        try {
            List<String> rows = new ArrayList<>();
            for (Object row : manager.createQuery(jpql).getResultList()) {
                Object[] values = (Object[]) row;
                rows.add(values[0] + "," + values[1]);
            }
            return rows;
        } finally {
            manager.close();
        }
    }
}
