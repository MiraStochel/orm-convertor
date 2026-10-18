package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

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
import org.eclipse.persistence.exceptions.QueryException;
import org.eclipse.persistence.sessions.Session;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 reads the {@code having} clause, the reason the profile carries
 * {@code RebuildsHavingOnTheRoot}: it takes the first operand of the clause that carries a
 * variable, read from the left - constants and parameters carry none -, and where that is a
 * path over another variable than the root of the block (an entity join, a further range
 * variable, the enclosing block), it rebuilds the whole clause on the root, so every path in
 * it names the root's attribute of that name: refused when the query runs where the root has
 * none (an invalid query key), other rows without an error where it has one. Where the first
 * is a path over the root, a CASE or a subquery, every path stands as written - the shape of
 * LDBC IC 3, which the validation set judged in EclipseLink. The select list, {@code group by}
 * and {@code order by} are never rebuilt. Measured first by the FIBEN catalog (decision 149:
 * 18 cells refused, every one opening its having clause with an aggregate over a joined
 * entity), pinned here shape by shape; the JPQL builder writes such a query in native SQL for
 * EclipseLink (decision 113).
 */
@Tag("integration")
class JoinedHavingClaimsTest {

    @Entity(name = "HvOrder")
    @Table(name = "HvOrders")
    public static class HvOrder {
        @Id
        @Column(name = "id")
        private Integer id;

        @Column(name = "customerId")
        private Integer customerId;
    }

    @Entity(name = "HvLine")
    @Table(name = "HvLines")
    public static class HvLine {
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

    /** Every query below groups the orders joined to their lines by customer. */
    private static final String BY_CUSTOMER =
            "select o.customerId, count(o.id) from HvOrder o join HvLine l on l.orderId = o.id group by o.customerId having ";

    private static EntityManagerFactory factory;

    /**
     * Two customers: 100 with the orders 1 (lines 1 and 2, quantities 8 and 5) and 2 (line 3,
     * quantity 4), 200 with the order 3 (line 4, quantity 20). The highest line of either
     * customer is above the highest order, so reading the order's key for the line's changes
     * the answer.
     */
    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".HvOrders (id int PRIMARY KEY, customerId int NOT NULL)");
            statement.execute("CREATE TABLE " + schema + ".HvLines (id int PRIMARY KEY, orderId int NOT NULL "
                    + "REFERENCES " + schema + ".HvOrders (id), quantity int NOT NULL)");
            statement.execute("INSERT INTO " + schema + ".HvOrders VALUES (1, 100), (2, 100), (3, 200)");
            statement.execute("INSERT INTO " + schema + ".HvLines VALUES (1, 1, 8), (2, 1, 5), (3, 2, 4), (4, 3, 20)");
        }

        factory = EclipseLinkBootstrap.build(
                "none",
                JoinedHavingClaimsTest.class.getClassLoader(),
                null,
                List.of(HvOrder.class, HvLine.class),
                Map.of("eclipselink.session.customizer", SuiteSchema.class.getName()));
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        if (factory != null) {
            factory.close();
        }
        TestSchema.drop();
    }

    // ---- a clause opening with a path over another variable is rebuilt on the root -------

    /**
     * The claim that makes it a matter of rows, not of an error: the joined entity's attribute
     * shares its name with one of the root, and the root's column is read in its place
     * ({@code HAVING (MAX(t0.id) > ?)}). The answer is customer 200; rebuilt, it is none.
     */
    @Test
    void aClauseOpeningWithAJoinedPathReadsTheRootsColumnOfTheSameName() {
        assertEquals(List.of(), rows(BY_CUSTOMER + "max(l.id) > 3"));
    }

    /** Where the root has no attribute of the name, the query is refused when it runs. */
    @Test
    void aClauseOpeningWithAJoinedPathIsRefusedWhereTheRootLacksTheAttribute() {
        assertRefused(BY_CUSTOMER + "sum(l.quantity) > 18");
        assertRefused("select o.customerId from HvOrder o join HvLine l on l.orderId = o.id group by o.customerId having sum(l.quantity) > 18");
        assertRefused(BY_CUSTOMER + "max(abs(l.quantity)) > 18");
    }

    /** A bare path counts as much as one under an aggregate. */
    @Test
    void aClauseOpeningWithABareJoinedPathIsRebuilt() {
        assertRefused("select l.orderId, count(o.id) from HvOrder o join HvLine l on l.orderId = o.id"
                + " group by l.orderId having l.orderId > 1");
    }

    /** A constant or a parameter on the left carries no variable, so the path on the right is the first. */
    @Test
    void aConstantOrAParameterIsReadPast() {
        assertRefused(BY_CUSTOMER + "18 < max(l.quantity)");
        EntityManager manager = factory.createEntityManager();
        try {
            assertThrows(QueryException.class, () -> manager.createQuery(BY_CUSTOMER + ":q < max(l.quantity)")
                    .setParameter("q", 18).getResultList());
        } finally {
            manager.close();
        }
    }

    /** Rebuilt, the clause loses every path, the ones after a path over the root included. */
    @Test
    void theRebuildTakesEveryPathOfTheClause() {
        assertRefused(BY_CUSTOMER + "max(l.quantity) > 18 and count(o.id) > 0");
        assertRefused(BY_CUSTOMER + "max(l.quantity) > count(o.id) * 4");
    }

    /** A further range variable of the from clause - the spelling the builder uses inside a subquery - is rebuilt as well. */
    @Test
    void aClauseOpeningWithARangeVariableIsRebuilt() {
        assertRefused("select o.customerId, count(o.id) from HvOrder o, HvLine l where l.orderId = o.id"
                + " group by o.customerId having sum(l.quantity) > 18");
        assertRefused("select o.customerId, o.id from HvOrder o where o.customerId in (select o2.customerId"
                + " from HvOrder o2, HvLine l2 where l2.orderId = o2.id group by o2.customerId having sum(l2.quantity) > 18)");
    }

    /** Inside a subquery a path over the enclosing block opening the clause is rebuilt on the subquery's root. */
    @Test
    void aClauseOpeningWithAPathOverTheEnclosingBlockIsRebuilt() {
        assertRefused("select o.id, o.customerId from HvOrder o where exists (select l2.orderId from HvLine l2"
                + " where l2.orderId = o.id group by l2.orderId having o.customerId - 95 < max(l2.quantity))");
    }

    // ---- a clause opening with the root, a CASE or a subquery stands as written -----------

    /** A path over the root first, and the joined path after it is read as written. */
    @Test
    void aClauseOpeningWithTheRootKeepsEveryPath() {
        assertEquals(List.of("100,3"), rows(BY_CUSTOMER + "count(o.id) > 1"));
        assertEquals(List.of("200,1"), rows(BY_CUSTOMER + "count(o.id) > 0 and max(l.id) > 3"));
        assertEquals(List.of("200,1"), rows(BY_CUSTOMER + "count(o.id) * 4 < max(l.quantity)"));
    }

    /** A CASE first - the shape of LDBC IC 3 and BI 16 - leaves the clause as written. */
    @Test
    void aClauseOpeningWithACaseKeepsEveryPath() {
        assertEquals(List.of("100,3"), rows(BY_CUSTOMER + "count(distinct case when l.quantity > 4 then l.id else null end) > 1"));
        assertEquals(List.of("200,1"), rows(BY_CUSTOMER + "count(distinct case when l.quantity > 18 then l.id else null end) > 0 and max(l.id) > 3"));
    }

    /** So does a subquery first. */
    @Test
    void aClauseOpeningWithASubqueryKeepsEveryPath() {
        assertEquals(List.of("100,3", "200,1"), rows(BY_CUSTOMER + "(select count(l3.id) from HvLine l3) < max(l.quantity) order by o.customerId"));
    }

    /** A path over the enclosing block after the subquery's root stands as written. */
    @Test
    void aPathOverTheEnclosingBlockAfterTheRootStands() {
        assertEquals(List.of("1,100"),
                rows("select o.id, o.customerId from HvOrder o where exists (select l2.orderId from HvLine l2"
                        + " where l2.orderId = o.id group by l2.orderId having max(l2.quantity) > o.customerId - 95)"
                        + " order by o.id"));
    }

    /** The select list, group by, order by and a subquery of the clause are not rebuilt. */
    @Test
    void theOtherClausesAndASubqueryOfTheClauseStandAsWritten() {
        assertEquals(List.of("1,2", "2,1", "3,1"),
                rows("select l.orderId, count(o.id) from HvOrder o join HvLine l on l.orderId = o.id"
                        + " group by l.orderId having count(o.id) > 0 order by l.orderId"));
        assertEquals(List.of("200,20", "100,17"),
                rows("select o.customerId, sum(l.quantity) from HvOrder o join HvLine l on l.orderId = o.id"
                        + " group by o.customerId order by sum(l.quantity) desc"));
        assertEquals(List.of("1,2", "2,1", "3,1"),
                rows("select l.orderId, count(o.id) from HvOrder o join HvLine l on l.orderId = o.id group by l.orderId"
                        + " having count(o.id) >= (select count(l2.id) from HvLine l2 where l2.orderId = l.orderId)"
                        + " order by l.orderId"));
    }

    private static void assertRefused(String jpql) {
        assertThrows(QueryException.class, () -> rows(jpql), jpql);
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
