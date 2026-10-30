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
 * What EclipseLink 5.0.0 does with comma-separated range variables - the spelling the JPQL
 * builder gives a cross join (decision 196): with no condition, with the condition in
 * {@code where}, inside a subquery, before an entity join and after one. Each query's rows
 * are compared with what the same T-SQL returns, because this release can return other
 * rows without an error ({@link JoinedHavingClaimsTest}); a refusal at {@code createQuery}
 * would be the other measurable answer.
 */
@Tag("integration")
class CrossJoinClaimsTest {

    @Entity(name = "CjCustomer")
    @Table(name = "CjCustomers")
    public static class CjCustomer {
        @Id
        @Column(name = "id")
        private Integer id;
    }

    @Entity(name = "CjOrder")
    @Table(name = "CjOrders")
    public static class CjOrder {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "customerId")
        private Integer customerId;
    }

    @Entity(name = "CjLine")
    @Table(name = "CjLines")
    public static class CjLine {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "orderId")
        private Integer orderId;
    }

    private static EntityManagerFactory factory;

    /** Customers 100 and 200; orders 1 and 2 of customer 100, order 3 of customer 200; lines 1 and 2 on order 1, line 3 on order 2, line 4 on order 3. */
    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".CjCustomers (id int PRIMARY KEY)");
            statement.execute("CREATE TABLE " + schema + ".CjOrders (id int PRIMARY KEY, customerId int NOT NULL "
                    + "REFERENCES " + schema + ".CjCustomers (id))");
            statement.execute("CREATE TABLE " + schema + ".CjLines (id int PRIMARY KEY, orderId int NOT NULL "
                    + "REFERENCES " + schema + ".CjOrders (id))");
            statement.execute("INSERT INTO " + schema + ".CjCustomers VALUES (100), (200)");
            statement.execute("INSERT INTO " + schema + ".CjOrders VALUES (1, 100), (2, 100), (3, 200)");
            statement.execute("INSERT INTO " + schema + ".CjLines VALUES (1, 1), (2, 1), (3, 2), (4, 3)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                CrossJoinClaimsTest.class.getClassLoader(),
                null,
                List.of(CjCustomer.class, CjOrder.class, CjLine.class),
                Map.of("eclipselink.session.customizer", JoinedHavingClaimsTest.SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    /** Three orders times four lines. */
    @Test
    void rangeVariablesWithoutACondition() {
        assertEquals(12, rows("select o.id, l.id from CjOrder o, CjLine l order by o.id, l.id").size());
    }

    /** The condition in where makes the inner join: every line with its order. */
    @Test
    void rangeVariablesWithTheConditionInWhere() {
        assertEquals(List.of("1,1", "1,2", "2,3", "3,4"),
                rows("select o.id, l.id from CjOrder o, CjLine l where l.orderId = o.id order by l.id"));
    }

    /** Inside a subquery: the order of customer 200 alone. */
    @Test
    void rangeVariablesInsideASubquery() {
        assertEquals(List.of("3,4"), rows("select o.id, l.id from CjOrder o join CjLine l on l.orderId = o.id"
                + " where o.id in (select l2.orderId from CjLine l2, CjOrder o2 where o2.id = l2.orderId and o2.customerId = 200) order by l.id"));
    }

    /** A range variable after an entity join, the join's condition naming the root. */
    @Test
    void aRangeVariableAfterAnEntityJoin() {
        assertEquals(List.of("1,1,100", "1,2,100", "2,3,100", "3,4,200"),
                rows("select o.id, l.id, c.id from CjOrder o join CjLine l on l.orderId = o.id, CjCustomer c where c.id = o.customerId order by l.id"));
    }

    /** An entity join after a range variable whose condition names that range variable. */
    @Test
    void anEntityJoinAfterARangeVariableMayNameIt() {
        assertEquals(List.of("1,1,100", "1,2,100", "2,3,100", "3,4,200"),
                rows("select o.id, l.id, c.id from CjCustomer c, CjOrder o join CjLine l on l.orderId = o.id where o.customerId = c.id order by l.id"));
    }

    /** An entity join after a range variable, the join's condition naming the first root. */
    @Test
    void anEntityJoinAfterARangeVariable() {
        assertEquals(List.of("1,1,100", "1,2,100", "2,3,100", "3,4,200"),
                rows("select o.id, l.id, c.id from CjOrder o, CjCustomer c join CjLine l on l.orderId = o.id where c.id = o.customerId order by l.id"));
    }

    private static List<String> rows(String jpql) {
        EntityManager manager = factory.createEntityManager();
        try {
            List<String> rows = new ArrayList<>();
            for (Object row : manager.createQuery(jpql).getResultList()) {
                Object[] values = (Object[]) row;
                StringBuilder line = new StringBuilder();
                for (Object value : values) {
                    if (line.length() > 0) {
                        line.append(',');
                    }
                    line.append(value);
                }
                rows.add(line.toString());
            }
            return rows;
        } finally {
            manager.close();
        }
    }
}
