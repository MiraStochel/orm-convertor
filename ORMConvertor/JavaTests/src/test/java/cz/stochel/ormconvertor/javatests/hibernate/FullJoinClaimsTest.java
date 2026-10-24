package cz.stochel.ormconvertor.javatests.hibernate;

import static org.junit.jupiter.api.Assertions.assertDoesNotThrow;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import org.hibernate.Session;
import org.hibernate.SessionFactory;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

/**
 * The other half of the profile fact {@code RefusesFullJoin} (decision 180), checked against
 * the release this JVM loads: HQL of Hibernate 7.4.5 reads {@code full join}, where the JPQL
 * parser of EclipseLink 5.0.0 refuses it - so the builder keeps the full join in HQL and
 * sends it to native SQL for EclipseLink alone. Measured by the experiment run of
 * 2026-10-24, whose Hibernate round trip accepted the query. Nothing is executed.
 */
@Tag("integration")
class FullJoinClaimsTest {

    @Entity(name = "FjCustomer")
    @Table(name = "FjCustomers")
    public static class FjCustomer {
        @Id
        @Column(name = "id")
        private Integer id;
    }

    @Entity(name = "FjOrder")
    @Table(name = "FjOrders")
    public static class FjOrder {
        @Id
        @Column(name = "id")
        private Integer id;

        @ManyToOne
        @JoinColumn(name = "customerId")
        private FjCustomer customer;
    }

    private static SessionFactory factory;
    private static Session session;

    @BeforeAll
    static void open() {
        factory = HibernateBootstrap.build("none", FjCustomer.class, FjOrder.class);
        session = factory.openSession();
    }

    @AfterAll
    static void close() {
        session.close();
        factory.close();
    }

    @Test
    void aFullJoinIsRead() {
        assertDoesNotThrow(() -> session.createSelectionQuery("select c from FjCustomer c full join FjOrder o on o.customer = c", Object.class));
        assertDoesNotThrow(() -> session.createSelectionQuery("select c from FjCustomer c full outer join FjOrder o on o.customer = c", Object.class));
    }
}
