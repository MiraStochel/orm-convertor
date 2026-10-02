package cz.stochel.ormconvertor.javatests.eclipselink;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EntityManager;
import jakarta.persistence.EntityManagerFactory;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.eclipse.persistence.sessions.SessionCustomizer;
import org.eclipse.persistence.sessions.Session;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How EclipseLink 5.0.0 reads a foreign key column no attribute maps, the shape a JPA
 * source usually has and the reason the profile carries {@code KeyThroughReferenceJoins}:
 * the key reached through the reference, {@code p.customer.id}, is an inner join of the
 * referenced table, so a purchase without a customer drops out - which is why the JPQL
 * builder writes a query that needs it in native SQL instead - while the reference compared
 * with the row, {@code p.customer = c}, the form the builder writes the condition of a join
 * along the association in, is read off the foreign key column and keeps every row of a
 * left join, in both directions. The entities are written by hand in the shape the builder
 * writes them; a purchase without a customer is the row that tells the two apart.
 */
@Tag("integration")
class ReferenceKeyClaimsTest {

    @Entity(name = "RefCustomer")
    @Table(name = "RefCustomers")
    public static class RefCustomer {
        @Id
        @Column(name = "CustomerID")
        private Integer id;
    }

    @Entity(name = "RefPurchase")
    @Table(name = "RefPurchases")
    public static class RefPurchase {
        @Id
        @Column(name = "id")
        private Integer id;

        @ManyToOne
        @JoinColumn(name = "customer_CustomerID", referencedColumnName = "CustomerID")
        private RefCustomer customer;
    }

    /** The hand-written entities name no schema, so the session qualifies their tables with the suite's. */
    public static final class SuiteSchema implements SessionCustomizer {
        @Override
        public void customize(Session session) {
            session.getLogin().setTableQualifier(TestDatabase.schemaName());
        }
    }

    @BeforeAll
    static void createTheTables() throws Exception {
        TestSchema.create();
        String schema = TestDatabase.schemaName();
        try (Connection connection = TestDatabase.open(); Statement statement = connection.createStatement()) {
            statement.execute("CREATE TABLE " + schema + ".RefCustomers (CustomerID int PRIMARY KEY)");
            statement.execute("CREATE TABLE " + schema + ".RefPurchases (id int PRIMARY KEY, customer_CustomerID int NULL "
                    + "REFERENCES " + schema + ".RefCustomers (CustomerID))");
            statement.execute("INSERT INTO " + schema + ".RefCustomers VALUES (1), (2), (3)");
            statement.execute("INSERT INTO " + schema + ".RefPurchases VALUES (10, 1), (11, 1), (12, 2), (13, NULL)");
        }
    }

    @AfterAll
    static void dropTheTables() throws Exception {
        TestSchema.drop();
    }

    /** The claim of the profile: the path joins Customers and the purchase without a customer drops out. */
    @Test
    void theKeyThroughTheReferenceIsAnInnerJoin() {
        assertEquals(List.of("10,1", "11,1", "12,2"), rows("select p.id, p.customer.id from RefPurchase p order by p.id"));
    }

    /** The form the builder writes a join along the association in keeps the purchase without a customer. */
    @Test
    void theReferenceComparedWithTheRowKeepsTheRowsOfALeftJoin() {
        assertEquals(List.of("10,1", "11,1", "12,2", "13,null"),
                rows("select p.id, c.id from RefPurchase p left join RefCustomer c on p.customer = c order by p.id"));
    }

    /** ... and the customer without a purchase, from the other side of the association. */
    @Test
    void theReferenceComparedWithTheRowKeepsTheRowsOfTheOtherDirection() {
        assertEquals(List.of("1,10", "1,11", "2,12", "3,null"),
                rows("select c.id, p.id from RefCustomer c left join RefPurchase p on p.customer = c order by c.id, p.id"));
    }

    private static List<String> rows(String jpql) {
        EntityManagerFactory factory = EclipseLinkBootstrap.build(
                "none",
                ReferenceKeyClaimsTest.class.getClassLoader(),
                null,
                List.of(RefCustomer.class, RefPurchase.class),
                Map.of("eclipselink.session.customizer", SuiteSchema.class.getName()));
        try {
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
        } finally {
            factory.close();
        }
    }
}
