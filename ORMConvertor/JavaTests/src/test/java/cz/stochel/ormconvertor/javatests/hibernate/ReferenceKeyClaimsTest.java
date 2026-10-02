package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertEquals;

import cz.stochel.ormconvertor.javatests.TestDatabase;
import cz.stochel.ormconvertor.javatests.TestSchema;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import java.sql.Connection;
import java.sql.Statement;
import java.util.ArrayList;
import java.util.List;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * How Hibernate 7.4.5 reads a foreign key column no attribute maps, the shape a JPA source
 * usually has: the key reached through the reference, {@code p.customer.id} - the form the
 * JPQL builder writes such a column in -, is read off the foreign key column, so a purchase
 * without a customer keeps its row, which is why the profile leaves
 * {@code KeyThroughReferenceJoins} unset; and the reference compared with the row,
 * {@code p.customer = c}, the form the builder writes the condition of a join along the
 * association in, keeps every row of a left join, in both directions. The entities are
 * written by hand in the shape the builder writes them.
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

    /** The path is the foreign key column itself: the purchase without a customer stays. */
    @Test
    void theKeyThroughTheReferenceIsTheForeignKeyColumn() {
        assertEquals(List.of("10,1", "11,1", "12,2", "13,null"),
                rows("select p.id, p.customer.id from RefPurchase p order by p.id"));
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
        try (SessionFactory factory = HibernateBootstrap.build("none", RefCustomer.class, RefPurchase.class);
             Session session = factory.openSession()) {
            List<String> rows = new ArrayList<>();
            for (Object[] row : session.createSelectionQuery(jpql, Object[].class).getResultList()) {
                rows.add(row[0] + "," + row[1]);
            }
            return rows;
        }
    }
}
